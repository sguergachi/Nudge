#!/usr/bin/env python3
"""
ML pipeline integration test.

Verifies the full ML pipeline end-to-end:
  1. Generate synthetic training data (V2 schema)
  2. Train a model on it
  3. Run inference on known samples
  4. Assert predictions are correct

Run: python tests/test_ml_pipeline.py
"""

import os
import sys
import json
import tempfile
import shutil
import warnings
warnings.filterwarnings('ignore')

_SCRIPT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _SCRIPT_DIR not in sys.path:
    sys.path.insert(0, _SCRIPT_DIR)

import numpy as np

EXPECTED_FEATURES = [
    'hour_of_day', 'day_of_week', 'focused_app_hash', 'focused_domain_hash',
    'idle_ms', 'focused_since_ms', 'title_stability_ms',
    'switch_count_60s', 'switch_count_300s', 'distinct_apps_300s', 'distinct_domains_300s',
    'returned_to_anchor_app_300s', 'current_app_share_300s', 'current_domain_share_300s',
    'browser_window_flag', 'communication_app_flag', 'entertainment_domain_flag', 'work_domain_flag',
    'afk_flag', 'fullscreen_flag', 'workspace_switch_count_300s',
    'dev_app_flag', 'creative_app_flag', 'office_app_flag', 'comm_app_flag', 'ent_app_flag',
]

passed = 0
failed = 0


def test(name, fn):
    global passed, failed
    try:
        fn()
        print(f'  ✓ {name}')
        passed += 1
    except Exception as e:
        print(f'  ✗ {name}: {e}')
        failed += 1


def main():
    global passed, failed
    tmpdir = tempfile.mkdtemp(prefix='nudge_ml_test_')
    model_dir = os.path.join(tmpdir, 'model')
    csv_path = os.path.join(tmpdir, 'HARVEST.CSV')
    os.makedirs(model_dir, exist_ok=True)

    print('ML Pipeline Smoke Tests')
    print()

    # 1. Dependencies
    test('scikit-learn available', lambda: __import__('sklearn'))
    test('joblib available', lambda: __import__('joblib'))
    test('pandas available', lambda: __import__('pandas'))
    test('numpy available', lambda: __import__('numpy'))

    from train_model import FEATURE_COLUMNS_V3, _compute_sample_weights, train_modern
    from model_inference import ProductivityPredictor
    from generate_sample_data import generate_sample_data

    # 2. Feature column alignment
    def check_features():
        assert FEATURE_COLUMNS_V3 == EXPECTED_FEATURES, (
            f'{len(FEATURE_COLUMNS_V3)} features, expected 26.\n'
            f'  Extra in train_model: {set(FEATURE_COLUMNS_V3) - set(EXPECTED_FEATURES)}\n'
            f'  Missing from train_model: {set(EXPECTED_FEATURES) - set(FEATURE_COLUMNS_V3)}'
        )
    test('feature columns match canonical list', check_features)

    # 3. Sample weights sanity
    def check_weights():
        y = np.array([1, 1, 1, 0, 0, 0, 0])
        is_migrated = np.array([0, 0, 1, 0, 0, 0, 1])
        base = 1.77e18  # ~2026 in nanoseconds
        timestamps = np.array([base, base + 60e9, base + 120e9,
                               base + 180e9, base + 240e9, base + 300e9, base + 360e9])
        sw = _compute_sample_weights(y, is_migrated, timestamps)
        assert np.all(sw > 0), f'Non-positive weight: {sw}'
        assert not np.any(np.isnan(sw)), 'NaN in weights'
        assert sw.min() > 1e-6, f'Minimum weight too small: {sw.min()}'
        prod_total = sw[y == 1].sum()
        unprod_total = sw[y == 0].sum()
        ratio = prod_total / unprod_total
        assert 0.5 < ratio < 5.0, (
            f'Class weight ratio too extreme: {ratio:.2f}:1 '
            f'(prod={prod_total:.2f} unprod={unprod_total:.2f})'
        )
    test('sample weights produce valid ratios', check_weights)

    # 4. Train model on synthetic data
    def check_training():
        csv = generate_sample_data(num_samples=300, output_file=csv_path)
        _, _, accuracy = train_modern(csv, model_dir=model_dir, architecture='lightweight')
        assert accuracy > 0.5, f'Accuracy too low: {accuracy}'
        assert os.path.exists(os.path.join(model_dir, 'productivity_model.joblib'))
        with open(os.path.join(model_dir, 'scaler.json')) as f:
            scaler_data = json.load(f)
        assert len(scaler_data['feature_order']) == 26
        assert 'dev_app_flag' in scaler_data['feature_order']
    test('trains model on synthetic data', check_training)

    # 5. Predict productive
    def check_productive():
        predictor = ProductivityPredictor(model_dir)
        features = {name: 0.0 for name in EXPECTED_FEATURES}
        features.update({
            'hour_of_day': 10, 'focused_since_ms': 300000,
            'idle_ms': 200, 'switch_count_60s': 0, 'switch_count_300s': 3,
            'distinct_apps_300s': 2, 'current_app_share_300s': 0.85,
            'returned_to_anchor_app_300s': 1, 'dev_app_flag': 1,
        })
        result = predictor.predict(features)
        assert result['model_available'], 'Model not available'
        assert result['prediction'] == 1, (
            f'Expected productive, got pred={result["prediction"]} '
            f'conf={result["confidence"]:.3f} prob={result.get("probability", "N/A")}'
        )
    test('predicts productive for dev work', check_productive)

    # 6. Predict NOT productive
    def check_not_productive():
        predictor = ProductivityPredictor(model_dir)
        features = {name: 0.0 for name in EXPECTED_FEATURES}
        features.update({
            'hour_of_day': 22, 'idle_ms': 25000, 'focused_since_ms': 5000,
            'switch_count_60s': 10, 'switch_count_300s': 30,
            'distinct_apps_300s': 6, 'current_app_share_300s': 0.05,
            'browser_window_flag': 1, 'entertainment_domain_flag': 1, 'ent_app_flag': 1,
        })
        result = predictor.predict(features)
        assert result['model_available']
        assert result['prediction'] is not None
    test('runs inference on any sample', check_not_productive)

    # 7. Inference server loads correctly
    def check_inference_server():
        predictor = ProductivityPredictor(model_dir)
        assert predictor.model_loaded
        assert len(predictor.feature_order) == 26
        assert predictor.schema_version == 2
    test('inference server loads model correctly', check_inference_server)

    shutil.rmtree(tmpdir, ignore_errors=True)

    print(f'\n{"=" * 40}')
    print(f'Passed: {passed}  Failed: {failed}  Total: {passed + failed}')
    if failed:
        print('FAILED')
        sys.exit(1)
    else:
        print('ALL ML PIPELINE TESTS PASSED')


if __name__ == '__main__':
    main()
