#!/usr/bin/env python3
"""
Background trainer for Nudge productivity model.

Watches HARVEST.CSV for new labeled samples and retrains the model automatically.
Runs as a daemon alongside the inference server and nudge-tray.

Usage:
  python background_trainer.py --csv ~/.nudge/HARVEST.CSV --model-dir ~/.nudge/model
"""

import os
import sys
import time
import json
import argparse

# Add the script's own directory to sys.path so train_model can be imported
# regardless of the working directory.
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if _SCRIPT_DIR not in sys.path:
    sys.path.insert(0, _SCRIPT_DIR)

DEFAULT_MIN_SAMPLES = 100
DEFAULT_CHECK_INTERVAL = 300  # 5 minutes
_RETRAIN_NEW_DATA_RATIO = 0.10  # retrain when 10% more data exists


def _count_labeled_rows(csv_path: str) -> int:
    """Count rows that have a non-empty 'productive' column."""
    try:
        with open(csv_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
        if len(lines) < 2:
            return 0
        header = lines[0].strip().split(',')
        if 'productive' not in header:
            return 0
        idx = header.index('productive')
        count = 0
        for line in lines[1:]:
            parts = line.strip().split(',')
            if len(parts) > idx and parts[idx].strip() not in ('', 'nan'):
                count += 1
        return count
    except Exception:
        return 0


def _model_exists(model_dir: str) -> bool:
    return os.path.exists(os.path.join(model_dir, 'productivity_model.joblib'))


def _load_meta(model_dir: str) -> dict:
    try:
        with open(os.path.join(model_dir, 'trainer_meta.json'), 'r') as f:
            return json.load(f)
    except Exception:
        return {}


def _save_meta(model_dir: str, sample_count: int, accuracy: float,
               model_version: int = 0) -> None:
    os.makedirs(model_dir, exist_ok=True)
    meta = {
        'trained_at': time.time(),
        'sample_count': sample_count,
        'accuracy': round(accuracy, 4),
        'model_version': model_version,
    }
    with open(os.path.join(model_dir, 'trainer_meta.json'), 'w') as f:
        json.dump(meta, f)


def _choose_architecture(sample_count: int) -> str:
    if sample_count < 200:
        return 'lightweight'
    if sample_count < 500:
        return 'standard'
    return 'deep'


def _run_training(csv_path: str, model_dir: str, sample_count: int) -> bool:
    try:
        from train_model import train_modern  # type: ignore

        arch = _choose_architecture(sample_count)
        print(f'[trainer] Training {arch} model on {sample_count} samples...', flush=True)

        os.makedirs(model_dir, exist_ok=True)
        _, _, accuracy = train_modern(
            csv_path,
            model_dir=model_dir,
            architecture=arch,
            mixed_precision=False,  # stable on CPU
            tensorboard=False,      # headless background process
            cpu_only=True,
        )

        # Read model version written by train_modern
        model_version = 0
        try:
            with open(os.path.join(model_dir, 'trainer_state.json'), 'r') as f:
                state = json.load(f)
                model_version = state.get('training_count', 0)
        except Exception:
            pass

        _save_meta(model_dir, sample_count, accuracy, model_version)
        print(f'[trainer] Done. accuracy={accuracy:.3f} version={model_version}', flush=True)
        return True

    except Exception as exc:
        import traceback
        print(f'[trainer] Training failed: {exc}', file=sys.stderr, flush=True)
        traceback.print_exc()
        return False


def _should_train(model_dir: str, current_count: int, min_samples: int,
                  force: bool = False) -> bool:
    if current_count < min_samples and not force:
        return False
    if not _model_exists(model_dir):
        return True
    if force:
        return True
    last_count = _load_meta(model_dir).get('sample_count', 0)
    threshold = last_count + max(10, int(last_count * _RETRAIN_NEW_DATA_RATIO))
    return current_count >= threshold


def main() -> None:
    parser = argparse.ArgumentParser(description='Nudge background model trainer')
    parser.add_argument('--csv', required=True, help='Path to HARVEST.CSV')
    parser.add_argument('--model-dir', required=True, help='Directory to save model')
    parser.add_argument('--check-interval', type=int, default=DEFAULT_CHECK_INTERVAL,
                        help=f'Seconds between checks (default: {DEFAULT_CHECK_INTERVAL})')
    parser.add_argument('--min-total-samples', type=int, default=DEFAULT_MIN_SAMPLES,
                        help=f'Minimum labeled samples before first train (default: {DEFAULT_MIN_SAMPLES})')
    parser.add_argument('--force', action='store_true',
                        help='Force training regardless of thresholds')
    args = parser.parse_args()

    csv_path = args.csv
    model_dir = args.model_dir
    interval = args.check_interval
    min_samples = args.min_total_samples
    force = args.force

    print(f'[trainer] Starting. csv={csv_path} model-dir={model_dir} '
          f'interval={interval}s min-samples={min_samples}', flush=True)

    while True:
        try:
            if not os.path.exists(csv_path):
                print(f'[trainer] Waiting for {csv_path}...', flush=True)
                time.sleep(interval)
                continue

            count = _count_labeled_rows(csv_path)
            last_count = _load_meta(model_dir).get('sample_count', 0)
            print(f'[trainer] Labeled samples: {count}  last-trained-at: {last_count}  min: {min_samples}',
                  flush=True)

            if _should_train(model_dir, count, min_samples, force=force):
                _run_training(csv_path, model_dir, count)
            else:
                print('[trainer] Nothing to do.', flush=True)

        except KeyboardInterrupt:
            print('[trainer] Shutting down.', flush=True)
            break
        except Exception as exc:
            print(f'[trainer] Unexpected error: {exc}', file=sys.stderr, flush=True)

        time.sleep(interval)


if __name__ == '__main__':
    main()
