#!/usr/bin/env python3
"""
MODERN TensorFlow 2.x trainer with latest acceleration techniques.

Features:
- Mixed precision training (2x faster on modern GPUs/CPUs)
- Learning rate scheduling (better convergence)
- TensorBoard integration (visualize training)
- Model checkpointing (save best model during training)
- AdamW optimizer (better than standard Adam)
- Batch normalization (faster training, better accuracy)
- Dropout (prevents overfitting)
- Functional API (more flexible architecture)
- Automatic performance optimization
"""

import pandas as pd
import numpy as np
import os
import sys
import warnings
from datetime import datetime

# Suppress warnings
os.environ['TF_CPP_MIN_LOG_LEVEL'] = '2'
warnings.filterwarnings('ignore')

import tensorflow as tf
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler

def configure_tensorflow_modern(mixed_precision=True, cpu_only=False):
    """Configure TensorFlow with modern optimizations"""

    if cpu_only:
        tf.config.set_visible_devices([], 'GPU')
        print("🖥️  CPU-only mode")
    else:
        gpus = tf.config.list_physical_devices('GPU')
        if gpus:
            print(f"🚀 GPU acceleration enabled: {len(gpus)} device(s)")
            for gpu in gpus:
                tf.config.experimental.set_memory_growth(gpu, True)

            # Enable mixed precision for 2x speedup on modern GPUs
            if mixed_precision:
                policy = tf.keras.mixed_precision.Policy('mixed_float16')
                tf.keras.mixed_precision.set_global_policy(policy)
                print("⚡ Mixed precision training enabled (2x faster!)")
        else:
            print("🖥️  CPU mode (no GPU detected)")

    # Enable XLA compilation for faster execution
    tf.config.optimizer.set_jit(True)
    print("🔥 XLA acceleration enabled")

def load_and_prepare_data(csv_file):
    """Load and prepare data with validation"""

    print(f"📂 Loading: {csv_file}")

    df = pd.read_csv(csv_file)

    required_cols = ['foreground_app', 'idle_time', 'time_last_request', 'productive']
    missing = [col for col in required_cols if col not in df.columns]
    if missing:
        raise ValueError(f"Missing columns: {missing}")

    print(f"   Loaded {len(df)} rows")

    df = df.dropna()
    print(f"   After cleaning: {len(df)} rows")

    if len(df) < 20:
        raise ValueError("Need at least 20 examples!")

    # Separate features and labels (3 features now: foreground_app, idle_time, attention_span)
    X = df[required_cols[:-1]].values.astype(np.float32)
    y = df['productive'].values.astype(np.int32)

    # Report class distribution
    prod = np.sum(y == 1)
    unprod = len(y) - prod
    print(f"\n📊 Dataset:")
    print(f"   Productive: {prod} ({prod/len(y)*100:.1f}%)")
    print(f"   Unproductive: {unprod} ({unprod/len(y)*100:.1f}%)")

    if prod == 0 or unprod == 0:
        raise ValueError("Need both productive AND unproductive examples!")

    return X, y

def build_modern_model(input_dim=3, architecture='standard', use_dropout=True, use_batchnorm=True):
    """
    Build modern neural network with latest best practices

    Args:
        input_dim: Number of input features
        architecture: 'lightweight', 'standard', or 'deep'
        use_dropout: Enable dropout for regularization
        use_batchnorm: Enable batch normalization
    """

    # Use Functional API for flexibility
    inputs = tf.keras.Input(shape=(input_dim,), name='input')
    x = inputs

    if architecture == 'lightweight':
        # Fast, low memory
        x = tf.keras.layers.Dense(8, activation='relu', name='dense_1')(x)
        if use_batchnorm:
            x = tf.keras.layers.BatchNormalization()(x)
        if use_dropout:
            x = tf.keras.layers.Dropout(0.2)(x)

    elif architecture == 'deep':
        # Best accuracy
        for i, units in enumerate([16, 12, 8]):
            x = tf.keras.layers.Dense(units, activation='relu', name=f'dense_{i+1}')(x)
            if use_batchnorm:
                x = tf.keras.layers.BatchNormalization()(x)
            if use_dropout:
                x = tf.keras.layers.Dropout(0.3)(x)

    else:  # standard
        # Balanced
        for i, units in enumerate([10, 10]):
            x = tf.keras.layers.Dense(units, activation='relu', name=f'dense_{i+1}')(x)
            if use_batchnorm:
                x = tf.keras.layers.BatchNormalization()(x)
            if use_dropout:
                x = tf.keras.layers.Dropout(0.2)(x)

    # Output layer (always float32 for numerical stability)
    outputs = tf.keras.layers.Dense(1, activation='sigmoid', dtype='float32', name='output')(x)

    model = tf.keras.Model(inputs=inputs, outputs=outputs, name=f'nudge_{architecture}')

    # Use AdamW optimizer (better than Adam)
    optimizer = tf.keras.optimizers.AdamW(
        learning_rate=0.001,
        weight_decay=0.01
    )

    model.compile(
        optimizer=optimizer,
        loss='binary_crossentropy',
        metrics=[
            'accuracy',
            tf.keras.metrics.Precision(name='precision'),
            tf.keras.metrics.Recall(name='recall')
        ]
    )

    return model

def create_callbacks(model_dir, tensorboard=True, early_stopping=True):
    """Create modern training callbacks"""

    callbacks = []

    # Model checkpointing - save best model
    checkpoint_path = os.path.join(model_dir, 'checkpoints', 'model_{epoch:02d}_{val_accuracy:.4f}.keras')
    os.makedirs(os.path.dirname(checkpoint_path), exist_ok=True)

    callbacks.append(tf.keras.callbacks.ModelCheckpoint(
        checkpoint_path,
        monitor='val_accuracy',
        save_best_only=True,
        mode='max',
        verbose=1
    ))

    # TensorBoard for visualization
    if tensorboard:
        log_dir = os.path.join(model_dir, 'logs', datetime.now().strftime("%Y%m%d-%H%M%S"))
        callbacks.append(tf.keras.callbacks.TensorBoard(
            log_dir=log_dir,
            histogram_freq=1,
            profile_batch='10,20'
        ))
        print(f"📈 TensorBoard logs: {log_dir}")
        print(f"   View with: tensorboard --logdir {model_dir}/logs")

    # Early stopping
    if early_stopping:
        callbacks.append(tf.keras.callbacks.EarlyStopping(
            monitor='val_loss',
            patience=15,
            restore_best_weights=True,
            verbose=1
        ))

    # Learning rate reduction on plateau
    callbacks.append(tf.keras.callbacks.ReduceLROnPlateau(
        monitor='val_loss',
        factor=0.5,
        patience=5,
        min_lr=0.00001,
        verbose=1
    ))

    return callbacks

def train_modern(csv_file, model_dir='./model', architecture='standard',
                mixed_precision=True, tensorboard=True, cpu_only=False):
    """
    Train with modern techniques and optimizations

    Args:
        csv_file: Path to training data
        model_dir: Output directory
        architecture: 'lightweight', 'standard', or 'deep'
        mixed_precision: Enable mixed precision (2x faster)
        tensorboard: Enable TensorBoard logging
        cpu_only: Force CPU-only mode
    """

    # Configure TensorFlow
    configure_tensorflow_modern(mixed_precision=mixed_precision, cpu_only=cpu_only)

    # Load data
    X, y = load_and_prepare_data(csv_file)

    # Normalize
    scaler = StandardScaler()
    X_scaled = scaler.fit_transform(X)

    # Split with stratification
    X_train, X_test, y_train, y_test = train_test_split(
        X_scaled, y, test_size=0.2, random_state=42, stratify=y
    )

    print(f"\n🎓 Training set: {len(X_train)} samples")
    print(f"🧪 Test set: {len(X_test)} samples")

    # Build model
    print(f"\n🏗️  Building {architecture} model...")
    model = build_modern_model(
        input_dim=X.shape[1],
        architecture=architecture,
        use_dropout=True,
        use_batchnorm=True
    )

    model.summary()

    # Create callbacks
    callbacks = create_callbacks(
        model_dir,
        tensorboard=tensorboard,
        early_stopping=True
    )

    # Train with modern practices
    print("\n🚀 Starting training...")
    print("=" * 60)

    history = model.fit(
        X_train, y_train,
        validation_split=0.2,
        epochs=150,  # More epochs but with early stopping
        batch_size=min(32, max(8, len(X_train) // 4)),
        callbacks=callbacks,
        verbose=1
    )

    # Evaluate
    print("\n" + "=" * 60)
    print("📊 Final Evaluation")
    print("=" * 60)

    results = model.evaluate(X_test, y_test, verbose=0)
    metrics_names = model.metrics_names

    for name, value in zip(metrics_names, results):
        print(f"{name:.<20} {value:.4f}")

    # Save final model
    os.makedirs(model_dir, exist_ok=True)
    model.save(os.path.join(model_dir, 'productivity_model.keras'))

    # Save scaler
    import json
    scaler_params = {
        'mean': scaler.mean_.tolist(),
        'scale': scaler.scale_.tolist()
    }
    with open(os.path.join(model_dir, 'scaler.json'), 'w') as f:
        json.dump(scaler_params, f)

    print(f"\n✅ Model saved to: {model_dir}/")

    # Test predictions
    print("\n🔮 Sample Predictions:")
    print("=" * 60)
    y_pred = (model.predict(X_test, verbose=0) > 0.5).astype(int).flatten()

    for i in range(min(5, len(X_test))):
        prob = model.predict(X_test[i:i+1], verbose=0)[0][0]
        print(f"  Sample {i+1}: Prob={prob:.3f}, Predicted={y_pred[i]}, Actual={y_test[i]} " +
              f"{'✓' if y_pred[i] == y_test[i] else '✗'}")

    return model, scaler, results[1]  # Return accuracy

if __name__ == '__main__':
    import argparse

    parser = argparse.ArgumentParser(
        description='Modern TensorFlow 2.x trainer with acceleration',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Standard modern training
  python train_model_modern.py /tmp/HARVEST.CSV

  # Deep model for best accuracy
  python train_model_modern.py /tmp/HARVEST.CSV --architecture deep

  # CPU-only without TensorBoard
  python train_model_modern.py /tmp/HARVEST.CSV --cpu-only --no-tensorboard

  # Lightweight for fast training
  python train_model_modern.py /tmp/HARVEST.CSV --architecture lightweight
        """
    )

    parser.add_argument('csv_file', nargs='?', default='/tmp/HARVEST.CSV')
    parser.add_argument('--model-dir', default='./model')
    parser.add_argument('--architecture', choices=['lightweight', 'standard', 'deep'],
                        default='standard', help='Model architecture')
    parser.add_argument('--no-mixed-precision', action='store_true',
                        help='Disable mixed precision training')
    parser.add_argument('--no-tensorboard', action='store_true',
                        help='Disable TensorBoard logging')
    parser.add_argument('--cpu-only', action='store_true',
                        help='Force CPU-only mode')

    args = parser.parse_args()

    if not os.path.exists(args.csv_file):
        print(f"❌ File not found: {args.csv_file}")
        sys.exit(1)

    print("=" * 60)
    print("🧠 NUDGE MODERN TRAINER")
    print("=" * 60)
    print(f"Architecture: {args.architecture}")
    print(f"Mixed Precision: {not args.no_mixed_precision}")
    print(f"TensorBoard: {not args.no_tensorboard}")
    print("=" * 60)

    try:
        model, scaler, accuracy = train_modern(
            args.csv_file,
            model_dir=args.model_dir,
            architecture=args.architecture,
            mixed_precision=not args.no_mixed_precision,
            tensorboard=not args.no_tensorboard,
            cpu_only=args.cpu_only
        )

        print("\n" + "=" * 60)
        if accuracy > 0.9:
            print("🏆 EXCELLENT ACCURACY!")
        elif accuracy > 0.75:
            print("✅ GOOD ACCURACY")
        else:
            print("⚠️  LOW ACCURACY - collect more data")
        print("=" * 60)

    except Exception as e:
        print(f"\n❌ Error: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)
