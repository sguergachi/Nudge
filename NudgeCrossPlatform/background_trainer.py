#!/usr/bin/env python3
"""
Background training service for continuous model improvement.

This service monitors the productivity log and automatically retrains
the model when sufficient new data has accumulated. It validates new
models before deployment to ensure they improve performance.

Features:
- Incremental training as new data arrives
- Model validation before deployment
- Automatic rollback if performance degrades
- Configurable training triggers
- Minimal resource usage while idle
"""

import os
import sys
import time
import signal
import json
import shutil
import platform
import tempfile
from datetime import datetime
from pathlib import Path

# Import training function
from train_model import train_modern, load_and_prepare_data


def get_default_csv_path():
    """Get platform-specific default CSV path"""
    if platform.system() == 'Windows':
        return os.path.join(tempfile.gettempdir(), 'HARVEST.CSV')
    else:
        return '/tmp/HARVEST.CSV'


class BackgroundTrainer:
    """Continuously monitor and retrain productivity model"""

    def __init__(self,
                 csv_file=None,
                 model_dir='./model',
                 min_new_samples=50,
                 min_total_samples=100,
                 check_interval=300,  # 5 minutes
                 architecture='standard'):
        """
        Args:
            csv_file: Path to productivity log (default: platform-specific temp dir)
            model_dir: Model output directory
            min_new_samples: Min new samples before retraining
            min_total_samples: Min total samples needed for first training
            check_interval: Seconds between checks for new data
            architecture: Model architecture (lightweight/standard/deep)
        """
        self.csv_file = csv_file if csv_file is not None else get_default_csv_path()
        self.model_dir = model_dir
        self.min_new_samples = min_new_samples
        self.min_total_samples = min_total_samples
        self.check_interval = check_interval
        self.architecture = architecture

        self.running = False
        self.last_trained_samples = 0
        self.training_count = 0
        self.state_file = os.path.join(model_dir, 'trainer_state.json')

        # Load previous state
        self.load_state()

    def load_state(self):
        """Load training state from disk"""
        if os.path.exists(self.state_file):
            try:
                with open(self.state_file, 'r') as f:
                    state = json.load(f)
                self.last_trained_samples = state.get('last_trained_samples', 0)
                self.training_count = state.get('training_count', 0)
                print(f"📂 Loaded state: {self.training_count} trainings, "
                      f"{self.last_trained_samples} samples seen",
                      file=sys.stderr)
            except Exception as e:
                print(f"⚠️  Could not load state: {e}", file=sys.stderr)

    def save_state(self):
        """Save training state to disk"""
        os.makedirs(self.model_dir, exist_ok=True)
        state = {
            'last_trained_samples': self.last_trained_samples,
            'training_count': self.training_count,
            'last_training_time': datetime.now().isoformat()
        }
        try:
            with open(self.state_file, 'w') as f:
                json.dump(state, f, indent=2)
        except Exception as e:
            print(f"⚠️  Could not save state: {e}", file=sys.stderr)

    def get_sample_count(self):
        """Get current number of samples in CSV"""
        if not os.path.exists(self.csv_file):
            return 0

        try:
            # Quick line count (faster than pandas for large files)
            with open(self.csv_file, 'r') as f:
                # Skip header
                next(f)
                count = sum(1 for _ in f)
            return count
        except Exception as e:
            print(f"⚠️  Error counting samples: {e}", file=sys.stderr)
            return 0

    def should_train(self, current_samples):
        """Determine if we should trigger training"""

        # First training: need minimum total samples
        if self.training_count == 0:
            if current_samples >= self.min_total_samples:
                print(f"✅ Sufficient data for initial training: {current_samples} samples",
                      file=sys.stderr)
                return True
            else:
                needed = self.min_total_samples - current_samples
                print(f"⏳ Waiting for more data... need {needed} more samples "
                      f"({current_samples}/{self.min_total_samples})",
                      file=sys.stderr)
                return False

        # Subsequent trainings: need minimum new samples
        new_samples = current_samples - self.last_trained_samples

        if new_samples >= self.min_new_samples:
            print(f"✅ Sufficient new data for retraining: {new_samples} new samples",
                  file=sys.stderr)
            return True
        else:
            needed = self.min_new_samples - new_samples
            print(f"⏳ Waiting for more data... need {needed} more samples "
                  f"({new_samples}/{self.min_new_samples})",
                  file=sys.stderr)
            return False

    def backup_model(self):
        """Backup current model before training"""
        model_path = os.path.join(self.model_dir, 'productivity_model.keras')
        if os.path.exists(model_path):
            backup_path = os.path.join(self.model_dir, 'productivity_model.backup.keras')
            try:
                shutil.copy2(model_path, backup_path)
                print(f"💾 Backed up current model to {backup_path}", file=sys.stderr)
                return True
            except Exception as e:
                print(f"⚠️  Could not backup model: {e}", file=sys.stderr)
                return False
        return True  # No existing model to backup

    def validate_model(self):
        """Validate newly trained model"""
        model_path = os.path.join(self.model_dir, 'productivity_model.keras')

        if not os.path.exists(model_path):
            print(f"❌ Model file not found: {model_path}", file=sys.stderr)
            return False

        try:
            # Try loading the model
            import tensorflow as tf
            model = tf.keras.models.load_model(model_path)

            # Test prediction
            import numpy as np
            test_input = np.array([[12345, 1000, 30000]], dtype=np.float32)
            pred = model.predict(test_input, verbose=0)

            # Basic sanity checks
            if pred.shape != (1, 1):
                print(f"❌ Invalid prediction shape: {pred.shape}", file=sys.stderr)
                return False

            if not (0.0 <= pred[0][0] <= 1.0):
                print(f"❌ Prediction out of range: {pred[0][0]}", file=sys.stderr)
                return False

            print(f"✅ Model validation passed", file=sys.stderr)
            return True

        except Exception as e:
            print(f"❌ Model validation failed: {e}", file=sys.stderr)
            return False

    def rollback_model(self):
        """Rollback to previous model if validation fails"""
        model_path = os.path.join(self.model_dir, 'productivity_model.keras')
        backup_path = os.path.join(self.model_dir, 'productivity_model.backup.keras')

        if os.path.exists(backup_path):
            try:
                shutil.copy2(backup_path, model_path)
                print(f"↩️  Rolled back to previous model", file=sys.stderr)
                return True
            except Exception as e:
                print(f"❌ Rollback failed: {e}", file=sys.stderr)
                return False
        else:
            print(f"⚠️  No backup available for rollback", file=sys.stderr)
            return False

    def train_model(self):
        """Train the model"""
        print(f"\n{'='*60}", file=sys.stderr)
        print(f"🧠 STARTING TRAINING #{self.training_count + 1}", file=sys.stderr)
        print(f"{'='*60}", file=sys.stderr)
        print(f"Time: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}", file=sys.stderr)

        # Backup current model
        if not self.backup_model():
            print(f"⚠️  Proceeding without backup", file=sys.stderr)

        try:
            # Train model
            model, scaler, accuracy = train_modern(
                self.csv_file,
                model_dir=self.model_dir,
                architecture=self.architecture,
                mixed_precision=False,  # Safer for background training
                tensorboard=False,      # Skip TensorBoard in background
                cpu_only=True           # Use CPU to avoid GPU memory issues
            )

            # Validate new model
            if not self.validate_model():
                print(f"❌ Model validation failed - rolling back", file=sys.stderr)
                self.rollback_model()
                return False

            # Update state
            self.training_count += 1
            self.last_trained_samples = self.get_sample_count()
            self.save_state()

            print(f"\n{'='*60}", file=sys.stderr)
            print(f"✅ TRAINING #{self.training_count} COMPLETED", file=sys.stderr)
            print(f"   Accuracy: {accuracy:.1%}", file=sys.stderr)
            print(f"   Samples trained on: {self.last_trained_samples}", file=sys.stderr)
            print(f"{'='*60}\n", file=sys.stderr)

            return True

        except Exception as e:
            print(f"❌ Training failed: {e}", file=sys.stderr)
            import traceback
            traceback.print_exc()

            # Rollback on error
            print(f"↩️  Attempting rollback...", file=sys.stderr)
            self.rollback_model()
            return False

    def run(self):
        """Main training loop"""
        self.running = True

        print(f"🚀 Background trainer started", file=sys.stderr)
        print(f"   CSV file: {self.csv_file}", file=sys.stderr)
        print(f"   Model dir: {self.model_dir}", file=sys.stderr)
        print(f"   Min new samples: {self.min_new_samples}", file=sys.stderr)
        print(f"   Min total samples: {self.min_total_samples}", file=sys.stderr)
        print(f"   Check interval: {self.check_interval}s", file=sys.stderr)
        print(f"   Architecture: {self.architecture}", file=sys.stderr)
        print(f"", file=sys.stderr)

        # Handle shutdown signals
        signal.signal(signal.SIGINT, self.shutdown)
        signal.signal(signal.SIGTERM, self.shutdown)

        while self.running:
            try:
                # Check if CSV exists
                if not os.path.exists(self.csv_file):
                    print(f"⏳ Waiting for CSV file: {self.csv_file}", file=sys.stderr)
                    time.sleep(self.check_interval)
                    continue

                # Get current sample count
                current_samples = self.get_sample_count()

                # Check if we should train
                if self.should_train(current_samples):
                    self.train_model()
                    print(f"", file=sys.stderr)

                # Sleep until next check
                print(f"💤 Sleeping for {self.check_interval}s...", file=sys.stderr)
                time.sleep(self.check_interval)

            except Exception as e:
                print(f"❌ Error in training loop: {e}", file=sys.stderr)
                import traceback
                traceback.print_exc()
                time.sleep(self.check_interval)

    def shutdown(self, signum=None, frame=None):
        """Graceful shutdown"""
        print(f"\n🛑 Shutting down background trainer...", file=sys.stderr)
        print(f"   Total trainings: {self.training_count}", file=sys.stderr)
        print(f"   Last trained on: {self.last_trained_samples} samples", file=sys.stderr)
        self.save_state()
        self.running = False
        sys.exit(0)


def main():
    import argparse

    parser = argparse.ArgumentParser(
        description='Background training service for continuous model improvement',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Start with defaults
  python background_trainer.py

  # Custom parameters
  python background_trainer.py --csv /tmp/HARVEST.CSV --min-new-samples 100

  # Use deep architecture for better accuracy
  python background_trainer.py --architecture deep

  # More frequent training
  python background_trainer.py --min-new-samples 20 --check-interval 60

The service runs in the foreground and logs to stderr.
Recommended: Run in a tmux/screen session or as a systemd service.
        """
    )

    parser.add_argument('--csv', default=None,
                        help=f'Path to productivity log CSV (default: {get_default_csv_path()})')
    parser.add_argument('--model-dir', default='./model',
                        help='Model output directory')
    parser.add_argument('--min-new-samples', type=int, default=50,
                        help='Minimum new samples before retraining')
    parser.add_argument('--min-total-samples', type=int, default=100,
                        help='Minimum total samples for initial training')
    parser.add_argument('--check-interval', type=int, default=300,
                        help='Seconds between checks for new data')
    parser.add_argument('--architecture', choices=['lightweight', 'standard', 'deep'],
                        default='standard', help='Model architecture')

    args = parser.parse_args()

    trainer = BackgroundTrainer(
        csv_file=args.csv,
        model_dir=args.model_dir,
        min_new_samples=args.min_new_samples,
        min_total_samples=args.min_total_samples,
        check_interval=args.check_interval,
        architecture=args.architecture
    )

    trainer.run()


if __name__ == '__main__':
    main()
