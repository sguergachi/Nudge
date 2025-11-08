#!/usr/bin/env python3
"""
Modern TensorFlow 2.x model trainer for Nudge productivity prediction.
Replaces the deprecated tf.contrib API from the original code.
"""

import pandas as pd
import numpy as np
import tensorflow as tf
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler
import sys
import os

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

def build_model():
    """Build a simple neural network for productivity prediction"""

    model = tf.keras.Sequential([
        tf.keras.layers.Dense(10, activation='relu', input_shape=(4,)),
        tf.keras.layers.Dense(10, activation='relu'),
        tf.keras.layers.Dense(1, activation='sigmoid')
    ])

    model.compile(
        optimizer='adam',
        loss='binary_crossentropy',
        metrics=['accuracy']
    )

    return model

def train_model(csv_file, model_dir='./model', test_size=0.2):
    """Train the productivity prediction model"""

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
    model = build_model()
    model.summary()

    # Train model
    print("\nTraining model...")
    history = model.fit(
        X_train, y_train,
        validation_split=0.2,
        epochs=100,
        batch_size=32,
        verbose=1,
        callbacks=[
            tf.keras.callbacks.EarlyStopping(
                monitor='val_loss',
                patience=10,
                restore_best_weights=True
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
    csv_file = sys.argv[1] if len(sys.argv) > 1 else '/tmp/HARVEST.CSV'

    if not os.path.exists(csv_file):
        print(f"❌ File not found: {csv_file}")
        print("\nUsage: python train_model.py [path_to_csv]")
        print("Example: python train_model.py /tmp/HARVEST.CSV")
        sys.exit(1)

    try:
        model, scaler, accuracy = train_model(csv_file)

        if accuracy < 0.6:
            print("\n⚠️  Warning: Model accuracy is low. You may need:")
            print("   - More training data")
            print("   - More diverse examples")
            print("   - Better feature engineering")
        elif accuracy > 0.95:
            print("\n⚠️  Warning: Accuracy is very high. This might indicate:")
            print("   - Overfitting")
            print("   - Data leakage")
            print("   - Very consistent patterns (which is good!)")
        else:
            print("\n✅ Model training completed successfully!")

    except Exception as e:
        print(f"\n❌ Error during training: {e}")
        sys.exit(1)
