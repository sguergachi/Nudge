#!/usr/bin/env python3
"""
Modern TensorFlow 2.x model trainer for Nudge productivity prediction.
Optimized for LOCAL cross-platform training with minimal performance requirements.

Features:
- CPU-only mode (no GPU required)
- Minimal memory footprint
- Quick training mode for fast iterations
- Works on Windows, Linux, macOS
- No cloud/GCP dependencies
"""

import pandas as pd
import numpy as np
import os
import sys
import warnings

# Suppress TensorFlow warnings for cleaner output
os.environ['TF_CPP_MIN_LOG_LEVEL'] = '2'
warnings.filterwarnings('ignore')

import tensorflow as tf
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler

# Force CPU-only mode if requested (reduces dependencies)
def configure_tensorflow(cpu_only=False):
    """Configure TensorFlow for optimal local performance"""
    if cpu_only:
        tf.config.set_visible_devices([], 'GPU')
        print("🖥️  Running in CPU-only mode (lower memory, no GPU needed)")
    else:
        # Use GPU if available, but don't fail if not
        gpus = tf.config.list_physical_devices('GPU')
        if gpus:
            print(f"🚀 GPU detected: {len(gpus)} device(s) - using GPU acceleration")
            # Enable memory growth to avoid hogging all GPU memory
            for gpu in gpus:
                tf.config.experimental.set_memory_growth(gpu, True)
        else:
            print("🖥️  No GPU detected - using CPU (this is fine for small datasets)")

    # Limit threads for better performance on low-end machines
    tf.config.threading.set_inter_op_parallelism_threads(2)
    tf.config.threading.set_intra_op_parallelism_threads(2)

def load_and_prepare_data(csv_file):
    """Load CSV data and prepare for training"""

    print(f"Loading data from {csv_file}...")

    # Load data
    df = pd.read_csv(csv_file)

    # Check for required columns
    required_cols = ['foreground_app', 'keyboard_activity', 'mouse_activity', 'time_last_request', 'productive']
    missing_cols = [col for col in required_cols if col not in df.columns]
    if missing_cols:
        raise ValueError(f"Missing required columns: {missing_cols}")

    print(f"Loaded {len(df)} rows")

    # Remove any rows with missing values
    df = df.dropna()
    print(f"After removing NaN: {len(df)} rows")

    if len(df) < 20:
        raise ValueError("Not enough data to train! Need at least 20 labeled examples.")

    # Separate features and labels
    feature_cols = ['foreground_app', 'keyboard_activity', 'mouse_activity', 'time_last_request']
    X = df[feature_cols].values.astype(np.float32)
    y = df['productive'].values.astype(np.int32)

    # Check class balance
    productive_count = np.sum(y == 1)
    unproductive_count = np.sum(y == 0)
    print(f"\nClass distribution:")
    print(f"  Productive: {productive_count} ({productive_count/len(y)*100:.1f}%)")
    print(f"  Not productive: {unproductive_count} ({unproductive_count/len(y)*100:.1f}%)")

    if productive_count == 0 or unproductive_count == 0:
        raise ValueError("Need examples of BOTH productive and unproductive behavior!")

    return X, y

def build_model(lightweight=False):
    """
    Build a neural network for productivity prediction

    Args:
        lightweight: If True, creates a smaller model (faster training, less memory)
    """
    if lightweight:
        # Lightweight model: fewer parameters, faster training
        model = tf.keras.Sequential([
            tf.keras.layers.Dense(8, activation='relu', input_shape=(4,)),
            tf.keras.layers.Dense(1, activation='sigmoid')
        ])
        print("📦 Using lightweight model (faster, lower memory)")
    else:
        # Standard model: better accuracy
        model = tf.keras.Sequential([
            tf.keras.layers.Dense(10, activation='relu', input_shape=(4,)),
            tf.keras.layers.Dense(10, activation='relu'),
            tf.keras.layers.Dense(1, activation='sigmoid')
        ])
        print("🎯 Using standard model (better accuracy)")

    model.compile(
        optimizer='adam',
        loss='binary_crossentropy',
        metrics=['accuracy']
    )

    return model

def train_model(csv_file, model_dir='./model', test_size=0.2, quick_mode=False, lightweight=False):
    """
    Train the productivity prediction model

    Args:
        csv_file: Path to CSV with training data
        model_dir: Where to save the model
        test_size: Fraction of data for testing (0.0-1.0)
        quick_mode: Fast training with fewer epochs (for testing/iteration)
        lightweight: Use smaller model (faster, less memory)
    """

    # Load and prepare data
    X, y = load_and_prepare_data(csv_file)

    # Normalize features
    scaler = StandardScaler()
    X_scaled = scaler.fit_transform(X)

    # Split data
    X_train, X_test, y_train, y_test = train_test_split(
        X_scaled, y, test_size=test_size, random_state=42, stratify=y
    )

    print(f"\nTraining set: {len(X_train)} samples")
    print(f"Test set: {len(X_test)} samples")

    # Build model
    print("\nBuilding model...")
    model = build_model(lightweight=lightweight)
    model.summary()

    # Configure training parameters
    if quick_mode:
        epochs = 30
        patience = 5
        print("⚡ Quick mode: Training for 30 epochs (faster iteration)")
    else:
        epochs = 100
        patience = 10
        print("🎓 Standard mode: Training for up to 100 epochs (better accuracy)")

    # Train model
    print("\nTraining model...")
    history = model.fit(
        X_train, y_train,
        validation_split=0.2,
        epochs=epochs,
        batch_size=min(32, len(X_train) // 2),  # Adaptive batch size
        verbose=1,
        callbacks=[
            tf.keras.callbacks.EarlyStopping(
                monitor='val_loss',
                patience=patience,
                restore_best_weights=True,
                verbose=1
            )
        ]
    )

    # Evaluate
    print("\nEvaluating on test set...")
    test_loss, test_accuracy = model.evaluate(X_test, y_test)
    print(f"\nTest Accuracy: {test_accuracy:.4f}")

    # Save model
    os.makedirs(model_dir, exist_ok=True)
    model.save(os.path.join(model_dir, 'productivity_model.keras'))

    # Save scaler parameters
    scaler_params = {
        'mean': scaler.mean_.tolist(),
        'scale': scaler.scale_.tolist()
    }
    import json
    with open(os.path.join(model_dir, 'scaler.json'), 'w') as f:
        json.dump(scaler_params, f)

    print(f"\n✅ Model saved to {model_dir}/")

    # Test predictions
    print("\n📊 Testing predictions on sample data:")
    for i in range(min(5, len(X_test))):
        prediction = model.predict(X_test[i:i+1], verbose=0)[0][0]
        actual = y_test[i]
        print(f"  Sample {i+1}: Predicted={prediction:.3f}, Actual={actual}, " +
              f"Class={'Productive' if prediction > 0.5 else 'Not Productive'}")

    return model, scaler, test_accuracy

if __name__ == '__main__':
    import argparse

    parser = argparse.ArgumentParser(
        description='Train Nudge productivity prediction model locally (cross-platform)',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Basic training
  python train_model.py /tmp/HARVEST.CSV

  # Quick mode for fast iteration
  python train_model.py /tmp/HARVEST.CSV --quick

  # Lightweight model for low-end machines
  python train_model.py /tmp/HARVEST.CSV --lightweight

  # CPU-only mode (no GPU setup needed)
  python train_model.py /tmp/HARVEST.CSV --cpu-only

  # Combine options for maximum speed
  python train_model.py /tmp/HARVEST.CSV --quick --lightweight --cpu-only
        """
    )

    parser.add_argument('csv_file', nargs='?', default='/tmp/HARVEST.CSV',
                        help='Path to CSV file with training data (default: /tmp/HARVEST.CSV)')
    parser.add_argument('--model-dir', default='./model',
                        help='Directory to save model (default: ./model)')
    parser.add_argument('--quick', action='store_true',
                        help='Quick training mode: fewer epochs, faster iteration')
    parser.add_argument('--lightweight', action='store_true',
                        help='Use lightweight model: smaller, faster, less memory')
    parser.add_argument('--cpu-only', action='store_true',
                        help='Force CPU-only mode (no GPU required)')

    args = parser.parse_args()

    # Configure TensorFlow
    configure_tensorflow(cpu_only=args.cpu_only)

    # Check file exists
    if not os.path.exists(args.csv_file):
        print(f"❌ File not found: {args.csv_file}")
        print("\nUsage: python train_model.py [path_to_csv] [options]")
        print("Run 'python train_model.py --help' for more options")
        sys.exit(1)

    print("=" * 60)
    print("🧠 NUDGE LOCAL MODEL TRAINER")
    print("=" * 60)
    print(f"📁 Data: {args.csv_file}")
    print(f"💾 Model output: {args.model_dir}")
    print("=" * 60)

    try:
        model, scaler, accuracy = train_model(
            args.csv_file,
            model_dir=args.model_dir,
            quick_mode=args.quick,
            lightweight=args.lightweight
        )

        print("\n" + "=" * 60)
        if accuracy < 0.6:
            print("⚠️  LOW ACCURACY")
            print("=" * 60)
            print("Your model accuracy is low. Try:")
            print("  • Collect more training data (aim for 100+ examples)")
            print("  • Ensure balanced classes (mix of productive/unproductive)")
            print("  • Be more consistent with labeling")
        elif accuracy > 0.95:
            print("⚠️  VERY HIGH ACCURACY")
            print("=" * 60)
            print("This might indicate:")
            print("  • Very consistent behavior patterns (good!)")
            print("  • Possible overfitting (collect more diverse data)")
        else:
            print("✅ SUCCESS!")
            print("=" * 60)
            print(f"Model trained successfully with {accuracy:.1%} accuracy")
            print(f"Ready to make predictions with: python predict.py")

        print("=" * 60)

    except Exception as e:
        print(f"\n❌ Error during training: {e}")
        import traceback
        if '--debug' in sys.argv:
            traceback.print_exc()
        sys.exit(1)
