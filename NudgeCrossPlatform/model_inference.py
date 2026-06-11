#!/usr/bin/env python3
"""
Real-time ML inference service for Nudge productivity predictions.

Loads a trained scikit-learn model and serves predictions over TCP.
Automatically reloads when the model file is updated by the background trainer.
"""

import os
import sys
import json
import socket
import signal
import threading
import time
import warnings
from pathlib import Path

warnings.filterwarnings('ignore')

import numpy as np

try:
    import joblib
    JOBLIB_AVAILABLE = True
except ImportError:
    JOBLIB_AVAILABLE = False
    print('joblib not available — running in fallback mode', file=sys.stderr)

MODEL_FILE = 'productivity_model.joblib'


class ProductivityPredictor:
    """Sklearn-based productivity predictor with confidence scoring."""

    def __init__(self, model_dir='./model'):
        self.model_dir   = model_dir
        self.model       = None
        self.scaler_mean  = None
        self.scaler_scale = None
        self.feature_order = None
        self.schema_version = None
        self.model_loaded   = False
        self._model_mtime   = 0.0
        self._lock = threading.Lock()
        self.load_model()

    def load_model(self) -> bool:
        model_path  = os.path.join(self.model_dir, MODEL_FILE)
        scaler_path = os.path.join(self.model_dir, 'scaler.json')

        if not os.path.exists(model_path):
            print(f'No model at {model_path} — waiting for trainer', file=sys.stderr)
            return False

        if not JOBLIB_AVAILABLE:
            print('joblib not available — cannot load model', file=sys.stderr)
            return False

        try:
            self.model = joblib.load(model_path)
            print(f'Model loaded from {model_path}', file=sys.stderr)

            if os.path.exists(scaler_path):
                with open(scaler_path) as f:
                    params = json.load(f)
                self.scaler_mean   = np.array(params['mean'])
                self.scaler_scale  = np.array(params['scale'])
                self.feature_order = params.get('feature_order')
                self.schema_version = params.get('schema_version')
                print(f'Scaler loaded from {scaler_path}', file=sys.stderr)
            else:
                print('No scaler found — using raw features', file=sys.stderr)

            self.model_loaded = True
            try:
                self._model_mtime = os.path.getmtime(model_path)
            except Exception:
                pass
            return True

        except Exception as e:
            print(f'Error loading model: {e}', file=sys.stderr)
            return False

    def check_and_reload(self):
        """Reload if the model file on disk is newer than what we loaded."""
        model_path = os.path.join(self.model_dir, MODEL_FILE)
        scaler_path = os.path.join(self.model_dir, 'scaler.json')
        try:
            if not os.path.exists(model_path):
                return
            mtime = os.path.getmtime(model_path)
            if mtime > self._model_mtime:
                # The trainer writes the model first and scaler.json last. A scaler
                # older than the model means we caught the set mid-write — wait for
                # the next poll, or we'd standardize with stale parameters and skew
                # every prediction until the following retrain.
                if os.path.exists(scaler_path) and os.path.getmtime(scaler_path) < mtime:
                    return
                print('Model file updated — reloading', file=sys.stderr)
                with self._lock:
                    self.load_model()
        except Exception as e:
            print(f'Model reload check error: {e}', file=sys.stderr)

    def predict(self, features_by_name, request_feature_order=None):
        if not self.model_loaded:
            return {'prediction': None, 'confidence': 0.0,
                    'model_available': False, 'reason': 'no_model'}
        try:
            feature_order = self.feature_order or request_feature_order
            if not feature_order:
                raise ValueError('missing_feature_order')

            X = np.array([[float(features_by_name.get(n, 0.0)) for n in feature_order]],
                         dtype=np.float32)

            if self.scaler_mean is not None and self.scaler_scale is not None:
                X = (X - self.scaler_mean) / self.scaler_scale

            prob       = float(self.model.predict_proba(X)[0][1])
            prediction = 1 if prob >= 0.5 else 0
            confidence = abs(prob - 0.5) * 2.0  # 0 = uncertain, 1 = certain

            return {'prediction': prediction, 'confidence': confidence,
                    'probability': prob, 'model_available': True}

        except Exception as e:
            print(f'Prediction error: {e}', file=sys.stderr)
            return {'prediction': None, 'confidence': 0.0,
                    'model_available': False, 'reason': str(e)}


class InferenceServer:
    """TCP inference server on 127.0.0.1:45002."""

    def __init__(self, host='127.0.0.1', port=45002, model_dir='./model'):
        self.host      = host
        self.port      = port
        self.predictor = ProductivityPredictor(model_dir)
        self.running   = False
        self.sock      = None
        self.request_count    = 0
        self.prediction_times = []

    def start(self):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind((self.host, self.port))
        self.sock.listen(5)
        self.sock.settimeout(1.0)
        self.running = True

        print(f'Inference server listening on {self.host}:{self.port}', file=sys.stderr)
        if self.predictor.model_loaded:
            print('Model ready', file=sys.stderr)
        else:
            print('No model yet — will reload when trainer produces one', file=sys.stderr)

        signal.signal(signal.SIGINT,  self.shutdown)
        signal.signal(signal.SIGTERM, self.shutdown)

        # Reload loop — picks up newly trained models every 10 s
        def _reload_loop():
            while self.running:
                time.sleep(10)
                self.predictor.check_and_reload()
        threading.Thread(target=_reload_loop, daemon=True).start()

        while self.running:
            try:
                conn, _ = self.sock.accept()
                threading.Thread(target=self.handle_client,
                                 args=(conn,), daemon=True).start()
            except socket.timeout:
                continue
            except Exception as e:
                if self.running:
                    print(f'Accept error: {e}', file=sys.stderr)

    def handle_client(self, conn):
        try:
            data = b''
            while True:
                chunk = conn.recv(1024)
                if not chunk:
                    break
                data += chunk
                if b'\n' in chunk:
                    break
            if not data:
                return

            request = json.loads(data.decode('utf-8'))

            if 'features' in request:
                features_by_name      = request.get('features', {})
                request_feature_order = request.get('feature_order', [])
            else:
                features_by_name = {
                    'hour_of_day':       request.get('hour_of_day', 0),
                    'day_of_week':       request.get('day_of_week', 0),
                    'foreground_app':    request.get('foreground_app', 0),
                    'idle_time':         request.get('idle_time', 0),
                    'time_last_request': request.get('time_last_request', 0),
                }
                request_feature_order = list(features_by_name.keys())

            t0 = time.time()
            with self.predictor._lock:
                result = self.predictor.predict(features_by_name, request_feature_order)
            ms = (time.time() - t0) * 1000

            self.prediction_times.append(ms)
            if len(self.prediction_times) > 100:
                self.prediction_times.pop(0)
            self.request_count += 1
            result['request_id']        = self.request_count
            result['prediction_time_ms'] = round(ms, 2)

            conn.sendall((json.dumps(result) + '\n').encode('utf-8'))

            if result.get('model_available'):
                label = 'PRODUCTIVE' if result['prediction'] == 1 else 'NOT_PRODUCTIVE'
                print(f'#{self.request_count}: {label} '
                      f'conf={result["confidence"]*100:.1f}% {ms:.1f}ms', file=sys.stderr)

        except Exception as e:
            print(f'Client error: {e}', file=sys.stderr)
            try:
                conn.sendall((json.dumps({'prediction': None, 'confidence': 0.0,
                                          'model_available': False, 'error': str(e)}) + '\n')
                             .encode('utf-8'))
            except Exception:
                pass
        finally:
            conn.close()

    def shutdown(self, signum=None, frame=None):
        print('Shutting down inference server', file=sys.stderr)
        if self.request_count > 0 and self.prediction_times:
            avg = sum(self.prediction_times) / len(self.prediction_times)
            print(f'Total requests: {self.request_count}  avg: {avg:.2f}ms', file=sys.stderr)
        self.running = False
        if self.sock:
            self.sock.close()
        sys.exit(0)


def test_client(host='127.0.0.1', port=45002):
    print('Testing inference server...')
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.connect((host, port))
        request = {'foreground_app': 12345, 'idle_time': 1000, 'time_last_request': 30000}
        sock.sendall((json.dumps(request) + '\n').encode('utf-8'))
        data = b''
        while True:
            chunk = sock.recv(1024)
            if not chunk or b'\n' in chunk:
                data += chunk
                break
            data += chunk
        result = json.loads(data.decode('utf-8'))
        print(json.dumps(result, indent=2))
        sock.close()
    except ConnectionRefusedError:
        print(f'Connection refused — is the server running on {host}:{port}?')
        sys.exit(1)


def main():
    import argparse
    parser = argparse.ArgumentParser(description='Nudge ML inference server')
    parser.add_argument('--model-dir', default='./model')
    parser.add_argument('--host', default='127.0.0.1')
    parser.add_argument('--port', type=int, default=45002)
    parser.add_argument('--test', action='store_true')
    args = parser.parse_args()

    if args.test:
        test_client(args.host, args.port)
    else:
        InferenceServer(args.host, args.port, args.model_dir).start()


if __name__ == '__main__':
    main()
