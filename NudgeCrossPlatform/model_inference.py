#!/usr/bin/env python3
"""
Real-time ML inference service for Nudge productivity predictions.

This service loads a trained TensorFlow model and provides predictions
via Unix domain socket. It returns both the prediction and confidence score
to enable the hybrid alert system (model-based + interval fallback).

Features:
- Unix domain socket IPC for fast communication
- Confidence scores for adaptive behavior
- Graceful model loading/fallback
- Low-latency predictions (<10ms)
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

# Suppress TensorFlow warnings
os.environ['TF_CPP_MIN_LOG_LEVEL'] = '2'
warnings.filterwarnings('ignore')

import numpy as np

# Try to import TensorFlow, but handle gracefully if not available
try:
    import tensorflow as tf
    TENSORFLOW_AVAILABLE = True
except ImportError:
    TENSORFLOW_AVAILABLE = False
    print("⚠️  TensorFlow not available - running in fallback mode", file=sys.stderr)


class ProductivityPredictor:
    """ML-powered productivity predictor with confidence scoring"""

    def __init__(self, model_dir='./model'):
        self.model_dir = model_dir
        self.model = None
        self.scaler_mean = None
        self.scaler_scale = None
        self.model_loaded = False
        self.load_model()

    def load_model(self):
        """Load trained model and scaler"""
        model_path = os.path.join(self.model_dir, 'productivity_model.keras')
        scaler_path = os.path.join(self.model_dir, 'scaler.json')

        if not os.path.exists(model_path):
            print(f"ℹ️  No model found at {model_path}", file=sys.stderr)
            print(f"   Run 'python train_model.py' to train a model first", file=sys.stderr)
            return False

        if not TENSORFLOW_AVAILABLE:
            print("❌ TensorFlow not available - cannot load model", file=sys.stderr)
            return False

        try:
            # Load model
            self.model = tf.keras.models.load_model(model_path)
            print(f"✅ Model loaded from {model_path}", file=sys.stderr)

            # Load scaler
            if os.path.exists(scaler_path):
                with open(scaler_path, 'r') as f:
                    scaler_params = json.load(f)
                self.scaler_mean = np.array(scaler_params['mean'])
                self.scaler_scale = np.array(scaler_params['scale'])
                print(f"✅ Scaler loaded from {scaler_path}", file=sys.stderr)
            else:
                print(f"⚠️  No scaler found - using raw features", file=sys.stderr)

            self.model_loaded = True
            return True

        except Exception as e:
            print(f"❌ Error loading model: {e}", file=sys.stderr)
            return False

    def predict(self, foreground_app, idle_time, time_last_request):
        """
        Make prediction with confidence score

        Args:
            foreground_app: Hash of foreground application
            idle_time: Idle time in milliseconds
            time_last_request: Time in current app (ms)

        Returns:
            dict with:
                - prediction: 0 (not productive) or 1 (productive)
                - confidence: probability of the prediction (0.0-1.0)
                - model_available: whether model was used
        """

        # Fallback if no model
        if not self.model_loaded:
            return {
                'prediction': None,
                'confidence': 0.0,
                'model_available': False,
                'reason': 'no_model'
            }

        try:
            # Prepare features
            features = np.array([[foreground_app, idle_time, time_last_request]], dtype=np.float32)

            # Scale features if scaler is available
            if self.scaler_mean is not None and self.scaler_scale is not None:
                features = (features - self.scaler_mean) / self.scaler_scale

            # Get prediction probability
            prob = self.model.predict(features, verbose=0)[0][0]

            # Convert to binary prediction
            prediction = 1 if prob >= 0.5 else 0

            # Confidence is how far from 0.5 (uncertain) the probability is
            # Range: 0.5 (no confidence) to 1.0 (absolute confidence)
            confidence = abs(prob - 0.5) * 2.0  # Scale to 0.0-1.0

            return {
                'prediction': int(prediction),
                'confidence': float(confidence),
                'probability': float(prob),
                'model_available': True
            }

        except Exception as e:
            print(f"❌ Prediction error: {e}", file=sys.stderr)
            return {
                'prediction': None,
                'confidence': 0.0,
                'model_available': False,
                'reason': str(e)
            }


class InferenceServer:
    """TCP socket server for ML predictions (cross-platform)"""

    def __init__(self, host='127.0.0.1', port=45002, model_dir='./model'):
        self.host = host
        self.port = port
        self.predictor = ProductivityPredictor(model_dir)
        self.running = False
        self.sock = None

        # Statistics
        self.request_count = 0
        self.prediction_times = []

    def start(self):
        """Start the inference server"""
        # Create TCP socket (cross-platform compatible)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind((self.host, self.port))
        self.sock.listen(5)
        self.sock.settimeout(1.0)  # Allow periodic cleanup checks

        self.running = True
        print(f"🚀 Inference server listening on {self.host}:{self.port}", file=sys.stderr)

        if self.predictor.model_loaded:
            print(f"✅ Model ready for predictions", file=sys.stderr)
        else:
            print(f"⚠️  Running in fallback mode (no model)", file=sys.stderr)

        # Handle shutdown signals
        signal.signal(signal.SIGINT, self.shutdown)
        signal.signal(signal.SIGTERM, self.shutdown)

        # Main server loop
        while self.running:
            try:
                conn, _ = self.sock.accept()
                threading.Thread(target=self.handle_client, args=(conn,), daemon=True).start()
            except socket.timeout:
                continue
            except Exception as e:
                if self.running:
                    print(f"❌ Accept error: {e}", file=sys.stderr)

    def handle_client(self, conn):
        """Handle client request"""
        try:
            # Read request (JSON)
            data = b''
            while True:
                chunk = conn.recv(1024)
                if not chunk:
                    break
                data += chunk
                if b'\n' in chunk:  # Newline-delimited JSON
                    break

            if not data:
                return

            # Parse request
            request = json.loads(data.decode('utf-8'))

            # Extract features
            foreground_app = request.get('foreground_app', 0)
            idle_time = request.get('idle_time', 0)
            time_last_request = request.get('time_last_request', 0)

            # Time prediction
            start_time = time.time()

            # Make prediction
            result = self.predictor.predict(foreground_app, idle_time, time_last_request)

            # Track performance
            prediction_time = (time.time() - start_time) * 1000  # ms
            self.prediction_times.append(prediction_time)
            if len(self.prediction_times) > 100:
                self.prediction_times.pop(0)

            self.request_count += 1

            # Add metadata
            result['request_id'] = self.request_count
            result['prediction_time_ms'] = round(prediction_time, 2)

            # Send response
            response = json.dumps(result) + '\n'
            conn.sendall(response.encode('utf-8'))

            # Log prediction
            if result.get('model_available'):
                pred_str = "PRODUCTIVE" if result['prediction'] == 1 else "NOT_PRODUCTIVE"
                conf_pct = result['confidence'] * 100
                print(f"📊 Request #{self.request_count}: {pred_str} "
                      f"(confidence: {conf_pct:.1f}%, {prediction_time:.1f}ms)",
                      file=sys.stderr)

        except Exception as e:
            print(f"❌ Client error: {e}", file=sys.stderr)
            error_response = json.dumps({
                'prediction': None,
                'confidence': 0.0,
                'model_available': False,
                'error': str(e)
            }) + '\n'
            try:
                conn.sendall(error_response.encode('utf-8'))
            except:
                pass
        finally:
            conn.close()

    def shutdown(self, signum=None, frame=None):
        """Graceful shutdown"""
        print(f"\n🛑 Shutting down inference server...", file=sys.stderr)

        # Print statistics
        if self.request_count > 0:
            avg_time = sum(self.prediction_times) / len(self.prediction_times)
            print(f"📊 Statistics:", file=sys.stderr)
            print(f"   Total requests: {self.request_count}", file=sys.stderr)
            print(f"   Avg prediction time: {avg_time:.2f}ms", file=sys.stderr)

        self.running = False
        if self.sock:
            self.sock.close()
        sys.exit(0)


def main():
    import argparse

    parser = argparse.ArgumentParser(
        description='ML inference service for Nudge productivity predictions',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Start inference server
  python model_inference.py

  # Use custom model directory
  python model_inference.py --model-dir /path/to/model

  # Use custom host/port
  python model_inference.py --host 0.0.0.0 --port 8080

  # Test prediction (client mode)
  python model_inference.py --test

The server runs in the foreground and logs predictions to stderr.
        """
    )

    parser.add_argument('--model-dir', default='./model',
                        help='Directory containing trained model')
    parser.add_argument('--host', default='127.0.0.1',
                        help='Host to bind to (default: 127.0.0.1)')
    parser.add_argument('--port', type=int, default=45002,
                        help='Port to bind to (default: 45002)')
    parser.add_argument('--test', action='store_true',
                        help='Test mode: send a sample prediction request')

    args = parser.parse_args()

    if args.test:
        # Test client mode
        test_client(args.host, args.port)
    else:
        # Start server
        server = InferenceServer(args.host, args.port, args.model_dir)
        server.start()


def test_client(host='127.0.0.1', port=45002):
    """Test client to verify inference server"""
    print("🧪 Testing inference server...")

    try:
        # Connect to server via TCP
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.connect((host, port))
        print(f"✅ Connected to {host}:{port}")

        # Send test request
        request = {
            'foreground_app': 12345,
            'idle_time': 1000,
            'time_last_request': 30000
        }

        print(f"📤 Sending: {request}")
        sock.sendall(json.dumps(request).encode('utf-8') + b'\n')

        # Receive response
        response = b''
        while True:
            chunk = sock.recv(1024)
            if not chunk:
                break
            response += chunk
            if b'\n' in chunk:
                break

        result = json.loads(response.decode('utf-8'))
        print(f"📥 Response: {json.dumps(result, indent=2)}")

        if result.get('model_available'):
            print(f"✅ Model is working!")
            pred_str = "PRODUCTIVE" if result['prediction'] == 1 else "NOT_PRODUCTIVE"
            print(f"   Prediction: {pred_str}")
            print(f"   Confidence: {result['confidence']*100:.1f}%")
        else:
            print(f"⚠️  Model not available: {result.get('reason', 'unknown')}")

        sock.close()

    except ConnectionRefusedError:
        print(f"❌ Connection refused to {host}:{port}")
        print(f"   Make sure the inference server is running!")
        sys.exit(1)
    except Exception as e:
        print(f"❌ Test failed: {e}")
        sys.exit(1)


if __name__ == '__main__':
    main()
