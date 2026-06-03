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
import tempfile
import argparse

# Add the script's own directory to sys.path so train_model can be imported
# regardless of the working directory.
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if _SCRIPT_DIR not in sys.path:
    sys.path.insert(0, _SCRIPT_DIR)

DEFAULT_MIN_SAMPLES = 10
DEFAULT_CHECK_INTERVAL = 15  # check for new labeled samples every 15 seconds


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
               model_version: int = 0, n_productive: int = 0, n_unproductive: int = 0) -> None:
    os.makedirs(model_dir, exist_ok=True)
    meta = {
        'trained_at': time.time(),
        'sample_count': sample_count,
        'accuracy': round(accuracy, 4),
        'model_version': model_version,
        'n_productive': n_productive,
        'n_unproductive': n_unproductive,
    }
    dest = os.path.join(model_dir, 'trainer_meta.json')
    with tempfile.NamedTemporaryFile(mode='w', dir=model_dir, suffix='.tmp', delete=False) as tf:
        json.dump(meta, tf)
        tmp = tf.name
    os.replace(tmp, dest)


def _run_training(csv_path: str, model_dir: str, sample_count: int,
                  max_arch: str | None = None) -> bool:
    """Train the model.

    Args:
        csv_path: Path to the labeled CSV.
        model_dir: Output directory for model files.
        sample_count: Real label count (saved in meta for retrain thresholding).
        max_arch: Cap architecture at this level (e.g. 'standard' for seed data).
    """
    try:
        from train_model import train_modern, load_and_prepare_data  # type: ignore

        arch = 'auto'
        if max_arch:
            # Seed data: cap at 'standard' even if auto picks 'deep'
            arch = max_arch
        print(f'[trainer] Training ({arch}) on {sample_count} samples...', flush=True)

        os.makedirs(model_dir, exist_ok=True)
        _, _, accuracy = train_modern(
            csv_path,
            model_dir=model_dir,
            architecture=arch,
            mixed_precision=False,  # stable on CPU
            tensorboard=False,      # headless background process
            cpu_only=True,
        )

        # Read model version and class counts
        model_version = 0
        n_productive = 0
        n_unproductive = 0
        try:
            with open(os.path.join(model_dir, 'trainer_state.json'), 'r') as f:
                state = json.load(f)
                model_version = state.get('training_count', 0)
            with open(os.path.join(model_dir, 'trainer_meta.json'), 'r') as f:
                meta = json.load(f)
                n_productive = meta.get('n_productive', 0)
                n_unproductive = meta.get('n_unproductive', 0)
        except Exception as e:
            print(f'[trainer] warn: could not read prior state/meta: {e}', file=sys.stderr, flush=True)

        _save_meta(model_dir, sample_count, accuracy, model_version, n_productive, n_unproductive)
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
    threshold = last_count + 20
    return current_count >= threshold


def _generate_seed_data(csv_path: str, target_samples: int,
                        seed_dir: str | None = None, schema: str = 'v3') -> str | None:
    """Generate synthetic seed data for model bootstrapping.

    Creates labeled V3-schema data and merges it with any existing real data
    from HARVEST.CSV. If HARVEST.CSV doesn't exist yet, just creates the seed
    file from scratch.

    Returns the path to a merged temp CSV (or the seed CSV if no merge was
    needed). Caller is responsible for cleanup via _cleanup_temp().
    """
    try:
        from generate_sample_data import generate_sample_data

        seed_dir = seed_dir or os.path.dirname(os.path.abspath(csv_path))
        os.makedirs(seed_dir, exist_ok=True)
        seed_path = os.path.join(seed_dir, f'.seed_{int(time.time())}.csv')
        generate_sample_data(num_samples=target_samples, output_file=seed_path, schema=schema)

        # If HARVEST.CSV exists with data, merge seed + real rows
        if os.path.exists(csv_path):
            with open(csv_path, 'r') as f:
                real_lines = f.readlines()
            if len(real_lines) > 1:  # header + at least 1 data row
                merged_path = seed_path.replace('.seed_', '.merged_')
                with open(seed_path, 'r') as sf:
                    seed_all = sf.readlines()
                with open(merged_path, 'w') as mf:
                    mf.write(seed_all[0])  # header from seed (same schema)
                    mf.writelines(seed_all[1:])   # seed data rows
                    mf.writelines(real_lines[1:])  # real data rows (skip real header)
                os.remove(seed_path)
                return merged_path

        # No real data to merge — just return the seed file
        return seed_path

    except Exception as exc:
        import traceback
        print(f'[trainer] Seed generation failed: {exc}', file=sys.stderr, flush=True)
        traceback.print_exc()
        return None


def _cleanup_temp(path: str | None) -> None:
    if path and os.path.exists(path) and ('.seed_' in path or '.merged_' in path):
        try:
            os.remove(path)
        except Exception as e:
            print(f'[trainer] warn: could not remove temp file {path}: {e}', file=sys.stderr, flush=True)


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
    parser.add_argument('--once', action='store_true',
                        help='Run a single training pass then exit')
    parser.add_argument('--seed', action='store_true',
                        help='Generate synthetic seed data if insufficient real labels exist')
    parser.add_argument('--schema', default='v3', choices=['v3', 'v4'],
                        help='Feature schema for synthetic seed generation (default: v3)')
    args = parser.parse_args()

    csv_path = args.csv
    model_dir = args.model_dir
    interval = args.check_interval
    min_samples = args.min_total_samples
    force = args.force
    once = args.once
    seed = args.seed
    schema = args.schema

    print(f'[trainer] Starting. csv={csv_path} model-dir={model_dir} '
          f'interval={interval}s min-samples={min_samples}', flush=True)
    if seed:
        print('[trainer] Seed mode: will generate synthetic data if insufficient real labels', flush=True)

    while True:
        try:
            csv_exists = os.path.exists(csv_path)
            real_count = _count_labeled_rows(csv_path) if csv_exists else 0
            model_exists = _model_exists(model_dir)
            using_seed = False
            train_path = csv_path if csv_exists else ''
            merged_path = None

            # Seed mode: bootstrap model when insufficient real data.
            # Handles both CSV-missing and CSV-with-few-labels.
            if seed and not model_exists and (not csv_exists or real_count < min_samples):
                needed = max(500, min_samples + 200)
                print(f'[trainer] Seed mode: {"no CSV" if not csv_exists else f"{real_count} real labels < {min_samples}"}, '
                      f'generating {needed} synthetic rows...', flush=True)
                merged = _generate_seed_data(csv_path, needed,
                                             seed_dir=os.path.dirname(csv_path), schema=schema)
                if merged:
                    train_path = merged
                    merged_path = merged
                    using_seed = True
                    # Ensure the real CSV exists with header for future
                    # daemon/trainer cycles
                    if not csv_exists:
                        os.makedirs(os.path.dirname(csv_path), exist_ok=True)
                        with open(merged, 'r') as sf:
                            header_line = sf.readline().strip()
                        with open(csv_path, 'w') as cf:
                            cf.write(header_line + '\n')
                        print(f'[trainer] Created {csv_path} with header for daemon', flush=True)
                    real_count = 0  # all labels are synthetic
            elif not csv_exists:
                print(f'[trainer] Waiting for {csv_path}...', flush=True)
                time.sleep(interval)
                continue

            train_count = _count_labeled_rows(train_path)
            last_count = _load_meta(model_dir).get('sample_count', 0)
            print(f'[trainer] Labeled samples: {train_count}  '
                  f'real: {real_count}  last-trained-at: {last_count}  min: {min_samples}',
                  flush=True)

            # Use real_count for threshold checks so retraining triggers correctly.
            # Seed training bypasses _should_train since merged data has enough
            # total samples even when real labels are few.
            if using_seed:
                print(f'[trainer] Initial seed training from merged data ({train_count} rows)',
                      flush=True)
                _run_training(train_path, model_dir, real_count, max_arch='standard')
            elif _should_train(model_dir, real_count, min_samples, force=force):
                _run_training(train_path, model_dir, real_count)
            else:
                print('[trainer] Nothing to do.', flush=True)

            if merged_path:
                _cleanup_temp(merged_path)

        except KeyboardInterrupt:
            print('[trainer] Shutting down.', flush=True)
            break
        except Exception as exc:
            print(f'[trainer] Unexpected error: {exc}', file=sys.stderr, flush=True)

        if once:
            break
        time.sleep(interval)


if __name__ == '__main__':
    main()
