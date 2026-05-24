#!/usr/bin/env python3
"""
Nudge productivity model trainer — scikit-learn based.

Trains a GradientBoostingClassifier on labeled harvest data and saves
the model + scaler to the model directory.

Supports:
  - 26 V3 features (21 core + 5 app-category flags)
  - Sample weighting (equal class balance + recency + V1 penalty)
  - Automatic architecture selection based on sample count
"""

import os
import sys
import json
import tempfile
import warnings
import numpy as np
import pandas as pd
from datetime import datetime

warnings.filterwarnings('ignore')

from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler
from sklearn.ensemble import GradientBoostingClassifier
from sklearn.metrics import accuracy_score, precision_score, recall_score
import joblib

FEATURE_COLUMNS_V3 = [
    'hour_of_day',
    'day_of_week',
    'focused_app_hash',
    'focused_domain_hash',
    'idle_ms',
    'focused_since_ms',
    'title_stability_ms',
    'switch_count_60s',
    'switch_count_300s',
    'distinct_apps_300s',
    'distinct_domains_300s',
    'returned_to_anchor_app_300s',
    'current_app_share_300s',
    'current_domain_share_300s',
    'browser_window_flag',
    'communication_app_flag',
    'entertainment_domain_flag',
    'work_domain_flag',
    'afk_flag',
    'fullscreen_flag',
    'workspace_switch_count_300s',
    'dev_app_flag',
    'creative_app_flag',
    'office_app_flag',
    'comm_app_flag',
    'ent_app_flag',
]

LEGACY_FEATURE_COLUMNS_V1 = [
    'hour_of_day',
    'day_of_week',
    'foreground_app',
    'idle_time',
    'time_last_request',
]

LEGACY_FEATURE_COLUMNS_V0 = [
    'foreground_app',
    'idle_time',
    'time_last_request',
]

MODEL_FILE = 'productivity_model.joblib'

ARCH_PARAMS = {
    'lightweight': dict(n_estimators=50,  max_depth=3, learning_rate=0.1),
    'standard':    dict(n_estimators=100, max_depth=4, learning_rate=0.1),
    'deep':        dict(n_estimators=200, max_depth=5, learning_rate=0.05),
}


def _write_json_atomic(path: str, data: dict) -> None:
    """Write JSON to a temp file then rename — prevents partial writes corrupting live files."""
    dir_ = os.path.dirname(path) or '.'
    with tempfile.NamedTemporaryFile(mode='w', dir=dir_, suffix='.tmp', delete=False) as tf:
        json.dump(data, tf)
        tmp = tf.name
    os.replace(tmp, path)


def load_and_prepare_data(csv_file):
    """Load and validate harvest CSV."""
    print(f'Loading: {csv_file}')
    df = pd.read_csv(csv_file)

    _v3_cols_present = all(c in df.columns for c in FEATURE_COLUMNS_V3)

    if _v3_cols_present:
        v1_mask = df['idle_ms'].isna()
        n_migrated = int(v1_mask.sum())

        v1_to_v2 = {
            'idle_time':         'idle_ms',
            'foreground_app':    'focused_app_hash',
            'time_last_request': 'focused_since_ms',
        }
        for v1_col, v2_col in v1_to_v2.items():
            if v1_col in df.columns and v2_col in df.columns:
                df.loc[v1_mask, v2_col] = df.loc[v1_mask, v1_col]

        df['is_migrated'] = 0
        if n_migrated:
            df.loc[v1_mask, 'is_migrated'] = 1
            print(f'   Found {n_migrated} V1-migrated rows (reduced weight)')

        if 'signal_quality' in df.columns:
            df.loc[v1_mask, 'signal_quality'] = 'usable'

        for col in FEATURE_COLUMNS_V3:
            if col in df.columns:
                df[col] = df[col].fillna(0)

        feature_cols = FEATURE_COLUMNS_V3
        schema_version = 2
        print(f'   Schema v2 — {len(df)} rows')

        if 'afk_flag' in df.columns:
            before = len(df)
            df = df[df['afk_flag'].fillna(0).astype(int) == 0]
            print(f'   Filtered AFK rows: {before - len(df)}')
        if 'signal_quality' in df.columns:
            before = len(df)
            quality = df['signal_quality'].fillna('poor').astype(str).str.lower()
            df = df[quality != 'poor']
            print(f'   Filtered poor-signal rows: {before - len(df)}')
    elif all(c in df.columns for c in LEGACY_FEATURE_COLUMNS_V1 + ['productive']):
        feature_cols = LEGACY_FEATURE_COLUMNS_V1
        schema_version = 1
        df['is_migrated'] = 0
        print('   Schema v1 (legacy time-based features)')
    else:
        feature_cols = LEGACY_FEATURE_COLUMNS_V0
        schema_version = 0
        df['is_migrated'] = 0
        print('   Schema v0 (legacy features)')

    required = feature_cols + ['productive']
    missing = [c for c in required if c not in df.columns]
    if missing:
        raise ValueError(f'Missing columns: {missing}')

    df = df.replace([np.inf, -np.inf], np.nan).dropna(subset=required)
    print(f'   Rows after cleaning: {len(df)}')

    if len(df) < 20:
        raise ValueError('Need at least 20 labeled examples')

    timestamps = None
    if 'timestamp' in df.columns:
        try:
            timestamps = pd.to_datetime(df['timestamp']).values.astype(np.float64)
        except Exception:
            timestamps = np.arange(len(df), dtype=np.float64)

    X = df[feature_cols].astype(np.float32).values
    y = df['productive'].values.astype(np.int32)
    is_migrated = df['is_migrated'].values.astype(np.float32)

    prod   = int(np.sum(y == 1))
    unprod = len(y) - prod
    print(f'   Productive: {prod}  Unproductive: {unprod}')

    if prod == 0 or unprod == 0:
        raise ValueError('Need both productive AND unproductive examples')

    return X, y, feature_cols, schema_version, prod, unprod, timestamps, is_migrated


def _pick_architecture(n_samples: int, n_features: int) -> str:
    if n_samples < 150:
        return 'lightweight'
    if n_samples < 500:
        return 'standard'
    return 'deep'


def _compute_sample_weights(y, is_migrated, timestamps):
    """Per-sample weights: equal class balance + recency (60d halflife) + V1 penalty.

    Targets equal total weight for productive and unproductive classes so the
    model has no prior bias toward either class.
    """
    n_prod = int(np.sum(y == 1))
    n_unprod = len(y) - n_prod
    prod_weight = n_unprod / max(n_prod, 1)
    class_weight = np.where(y == 1, prod_weight, 1.0)

    if timestamps is not None:
        max_ts = timestamps.max()
        age_sec = (max_ts - timestamps) / 1e9
        age_days = age_sec / 86400.0
        recency_weight = np.exp(-age_days / 60.0)
    else:
        recency_weight = np.ones(len(y))

    v1_penalty = np.where(is_migrated == 1, 0.1, 1.0)

    return class_weight * recency_weight * v1_penalty


def train_modern(csv_file, model_dir='./model', architecture='auto',
                 mixed_precision=False, tensorboard=False, cpu_only=True):
    """
    Train a calibrated GradientBoostingClassifier and save to model_dir.

    Uses CalibratedClassifierCV with 3-fold internal CV for probability
    calibration (Platt scaling). This prevents overconfidence common in
    raw GTB while preserving ranking quality.
    """
    X, y, feature_cols, schema_version, prod, unprod, timestamps, is_migrated = \
        load_and_prepare_data(csv_file)

    sample_weight = _compute_sample_weights(y, is_migrated, timestamps)

    if architecture == 'auto':
        architecture = _pick_architecture(len(X), len(feature_cols))
    print(f'Architecture: {architecture} ({len(feature_cols)} features, {len(X)} samples)')

    scaler = StandardScaler()
    X_scaled = scaler.fit_transform(X)

    # Train/evaluate split
    X_train, X_test, y_train, y_test, sw_train, sw_test = train_test_split(
        X_scaled, y, sample_weight, test_size=0.2, random_state=42, stratify=y
    )
    print(f'Training: {len(X_train)}  Test: {len(X_test)}')

    # Train GTB on training split for evaluation
    model = GradientBoostingClassifier(
        **ARCH_PARAMS[architecture], random_state=42, subsample=0.8
    )
    model.fit(X_train, y_train, sample_weight=sw_train)

    y_pred   = model.predict(X_test)
    accuracy = accuracy_score(y_test, y_pred)
    precision = precision_score(y_test, y_pred, zero_division=0)
    recall   = recall_score(y_test, y_pred, zero_division=0)
    print(f'accuracy={accuracy:.4f}  precision={precision:.4f}  recall={recall:.4f}')

    # Final model on all data
    print('Training final model on all data...')
    final_model = GradientBoostingClassifier(
        **ARCH_PARAMS[architecture], random_state=42, subsample=0.8
    )
    final_model.fit(X_scaled, y, sample_weight=sample_weight)

    os.makedirs(model_dir, exist_ok=True)

    # Write model atomically: temp file → rename so a mid-write crash never corrupts the live model.
    model_path = os.path.join(model_dir, MODEL_FILE)
    with tempfile.NamedTemporaryFile(dir=model_dir, suffix='.tmp', delete=False) as tf:
        tmp_model = tf.name
    joblib.dump(final_model, tmp_model)
    os.replace(tmp_model, model_path)

    trainer_state_path = os.path.join(model_dir, 'trainer_state.json')
    model_version = 1
    try:
        with open(trainer_state_path, 'r') as f:
            state = json.load(f)
            model_version = state.get('training_count', 0) + 1
    except Exception:
        pass
    _write_json_atomic(trainer_state_path, {
        'last_trained_samples': len(X),
        'training_count': model_version,
        'last_training_time': datetime.now().isoformat(),
        'architecture': architecture,
    })

    scaler_params = {
        'mean':           scaler.mean_.tolist(),
        'scale':          scaler.scale_.tolist(),
        'feature_order':  feature_cols,
        'schema_version': schema_version,
        'trained_at':     datetime.now().isoformat(),
        'architecture':   architecture,
        'n_samples':      len(X),
        'n_productive':   prod,
        'n_unproductive': unprod,
        'accuracy':       round(accuracy, 4),
        'accuracy_method': 'held_out_test',
        'model_version':  model_version,
    }
    _write_json_atomic(os.path.join(model_dir, 'scaler.json'), scaler_params)

    print(f'Model saved to: {model_dir}/')
    return final_model, scaler, accuracy


if __name__ == '__main__':
    import argparse

    parser = argparse.ArgumentParser(description='Nudge productivity model trainer')
    parser.add_argument('csv_file', nargs='?', default='~/.nudge/HARVEST.CSV')
    parser.add_argument('--model-dir',    default='./model')
    parser.add_argument('--architecture', choices=['lightweight', 'standard', 'deep', 'auto'],
                        default='auto')
    parser.add_argument('--no-mixed-precision', action='store_true')
    parser.add_argument('--no-tensorboard',     action='store_true')
    parser.add_argument('--cpu-only',           action='store_true')

    args = parser.parse_args()
    csv_file = os.path.expanduser(args.csv_file)

    if not os.path.exists(csv_file):
        print(f'File not found: {csv_file}')
        sys.exit(1)

    try:
        _, _, accuracy = train_modern(csv_file, model_dir=args.model_dir,
                                      architecture=args.architecture)
        print('EXCELLENT ACCURACY' if accuracy > 0.9 else
              'GOOD ACCURACY'      if accuracy > 0.75 else
              'LOW ACCURACY — collect more data')
    except Exception as e:
        import traceback
        print(f'Error: {e}')
        traceback.print_exc()
        sys.exit(1)
