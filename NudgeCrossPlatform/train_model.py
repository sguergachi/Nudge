#!/usr/bin/env python3
"""
Nudge productivity model trainer — scikit-learn based.

Trains a GradientBoostingClassifier on labeled harvest data and saves
the model + scaler to the model directory.
"""

import os
import sys
import json
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

FEATURE_COLUMNS_V2 = [
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


def load_and_prepare_data(csv_file):
    """Load and validate harvest CSV, returning X, y, feature_cols, schema_version."""
    print(f'Loading: {csv_file}')
    df = pd.read_csv(csv_file)

    _v2_cols_present = all(c in df.columns for c in FEATURE_COLUMNS_V2)

    if _v2_cols_present:
        # Migrate V1-format rows (NaN in V2 columns) using available V1 equivalents,
        # then zero-fill any remaining gaps so all rows are usable as V2.
        # Identify V1 rows (those lacking native V2 data)
        v1_mask = df['idle_ms'].isna()

        # Map V1 columns to their V2 equivalents
        v1_to_v2 = {
            'idle_time':         'idle_ms',
            'foreground_app':    'focused_app_hash',
            'time_last_request': 'focused_since_ms',
        }
        for v1_col, v2_col in v1_to_v2.items():
            if v1_col in df.columns and v2_col in df.columns:
                df.loc[v1_mask, v2_col] = df.loc[v1_mask, v1_col]

        # Mark migrated rows as 'usable' so they aren't filtered by the quality check
        if 'signal_quality' in df.columns:
            df.loc[v1_mask, 'signal_quality'] = 'usable'

        # Zero-fill any remaining NaN in V2 feature columns
        for col in FEATURE_COLUMNS_V2:
            if col in df.columns:
                df[col] = df[col].fillna(0)

        n_migrated = int(v1_mask.sum())
        _v2_usable_rows = int(df[FEATURE_COLUMNS_V2[4]].notna().sum())
        if n_migrated:
            print(f'   Migrated {n_migrated} V1 rows to V2 (best-effort)')

        feature_cols = FEATURE_COLUMNS_V2
        schema_version = 2
        print(f'   Schema v2 — {_v2_usable_rows} usable rows')

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
        print('   Schema v1 (legacy time-based features)')
    else:
        feature_cols = LEGACY_FEATURE_COLUMNS_V0
        schema_version = 0
        print('   Schema v0 (legacy features)')

    required = feature_cols + ['productive']
    missing = [c for c in required if c not in df.columns]
    if missing:
        raise ValueError(f'Missing columns: {missing}')

    df = df.replace([np.inf, -np.inf], np.nan).dropna(subset=required)
    print(f'   Rows after cleaning: {len(df)}')

    if len(df) < 20:
        raise ValueError('Need at least 20 labeled examples')

    X = df[feature_cols].astype(np.float32).values
    y = df['productive'].values.astype(np.int32)

    prod   = int(np.sum(y == 1))
    unprod = len(y) - prod
    print(f'   Productive: {prod}  Unproductive: {unprod}')

    if prod == 0 or unprod == 0:
        raise ValueError('Need both productive AND unproductive examples')

    return X, y, feature_cols, schema_version


def _build_model(architecture: str) -> GradientBoostingClassifier:
    """Map architecture name to GradientBoostingClassifier hyperparameters."""
    params = {
        'lightweight': dict(n_estimators=50,  max_depth=3, learning_rate=0.1),
        'standard':    dict(n_estimators=100, max_depth=4, learning_rate=0.1),
        'deep':        dict(n_estimators=200, max_depth=5, learning_rate=0.05),
    }
    p = params.get(architecture, params['standard'])
    return GradientBoostingClassifier(**p, random_state=42, subsample=0.8)


def train_modern(csv_file, model_dir='./model', architecture='standard',
                 mixed_precision=False, tensorboard=False, cpu_only=True):
    """
    Train a GradientBoostingClassifier and save to model_dir.

    Parameters mirror the old TensorFlow version so callers don't need changes.
    mixed_precision / tensorboard / cpu_only are accepted but ignored.
    """
    print(f'Architecture: {architecture}')

    X, y, feature_cols, schema_version = load_and_prepare_data(csv_file)

    scaler   = StandardScaler()
    X_scaled = scaler.fit_transform(X)

    X_train, X_test, y_train, y_test = train_test_split(
        X_scaled, y, test_size=0.2, random_state=42, stratify=y
    )
    print(f'Training: {len(X_train)}  Test: {len(X_test)}')

    model = _build_model(architecture)
    print(f'Training {architecture} model...')
    model.fit(X_train, y_train)

    y_pred    = model.predict(X_test)
    accuracy  = accuracy_score(y_test, y_pred)
    precision = precision_score(y_test, y_pred, zero_division=0)
    recall    = recall_score(y_test, y_pred, zero_division=0)
    print(f'accuracy={accuracy:.4f}  precision={precision:.4f}  recall={recall:.4f}')

    os.makedirs(model_dir, exist_ok=True)
    joblib.dump(model, os.path.join(model_dir, MODEL_FILE))

    # Track model version across training runs
    trainer_state_path = os.path.join(model_dir, 'trainer_state.json')
    model_version = 1
    try:
        with open(trainer_state_path, 'r') as f:
            state = json.load(f)
            model_version = state.get('training_count', 0) + 1
    except Exception:
        pass
    with open(trainer_state_path, 'w') as f:
        json.dump({
            'last_trained_samples': len(X),
            'training_count': model_version,
            'last_training_time': datetime.now().isoformat(),
        }, f)

    scaler_params = {
        'mean':           scaler.mean_.tolist(),
        'scale':          scaler.scale_.tolist(),
        'feature_order':  feature_cols,
        'schema_version': schema_version,
        'trained_at':     datetime.now().isoformat(),
        'architecture':   architecture,
        'n_samples':      len(X),
        'accuracy':       round(accuracy, 4),
        'model_version':  model_version,
    }
    with open(os.path.join(model_dir, 'scaler.json'), 'w') as f:
        json.dump(scaler_params, f)

    print(f'Model saved to: {model_dir}/')
    return model, scaler, accuracy


if __name__ == '__main__':
    import argparse

    parser = argparse.ArgumentParser(description='Nudge productivity model trainer')
    parser.add_argument('csv_file', nargs='?', default='~/.nudge/HARVEST.CSV')
    parser.add_argument('--model-dir',    default='./model')
    parser.add_argument('--architecture', choices=['lightweight', 'standard', 'deep'],
                        default='standard')
    # kept for CLI compatibility; ignored
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
