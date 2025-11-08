#!/usr/bin/env python3
"""
Real-time productivity prediction using the trained model.
Can be integrated with the Harvester to automatically detect when you're distracted.
"""

import tensorflow as tf
import numpy as np
import json
import sys
import os

class ProductivityPredictor:
    def __init__(self, model_dir='./model'):
        """Load trained model and scaler"""

        model_path = os.path.join(model_dir, 'productivity_model.keras')
        scaler_path = os.path.join(model_dir, 'scaler.json')

        if not os.path.exists(model_path):
            raise FileNotFoundError(f"Model not found: {model_path}. Train a model first!")

        if not os.path.exists(scaler_path):
            raise FileNotFoundError(f"Scaler not found: {scaler_path}. Train a model first!")

        # Load model
        self.model = tf.keras.models.load_model(model_path)

        # Load scaler parameters
        with open(scaler_path, 'r') as f:
            scaler_params = json.load(f)
            self.scaler_mean = np.array(scaler_params['mean'])
            self.scaler_scale = np.array(scaler_params['scale'])

        print("✓ Model loaded successfully")

    def normalize(self, features):
        """Normalize features using saved scaler parameters"""
        return (features - self.scaler_mean) / self.scaler_scale

    def predict(self, foreground_app, keyboard_activity, mouse_activity, time_last_request):
        """
        Predict productivity based on current activity.

        Args:
            foreground_app: Hash of the foreground application name
            keyboard_activity: Milliseconds since last keyboard activity
            mouse_activity: Milliseconds since last mouse activity
            time_last_request: Milliseconds in current application (attention span)

        Returns:
            tuple: (probability, is_productive, confidence)
        """

        # Prepare features
        features = np.array([[
            float(foreground_app),
            float(keyboard_activity),
            float(mouse_activity),
            float(time_last_request)
        ]])

        # Normalize
        features_scaled = self.normalize(features)

        # Predict
        probability = self.model.predict(features_scaled, verbose=0)[0][0]

        is_productive = probability > 0.5
        confidence = probability if is_productive else (1 - probability)

        return probability, is_productive, confidence

    def explain_prediction(self, foreground_app, keyboard_activity, mouse_activity, time_last_request):
        """Provide human-readable explanation of prediction"""

        prob, productive, conf = self.predict(
            foreground_app, keyboard_activity, mouse_activity, time_last_request
        )

        print("\n" + "="*50)
        print("PRODUCTIVITY PREDICTION")
        print("="*50)
        print(f"Foreground App Hash: {foreground_app}")
        print(f"Keyboard Inactive:   {keyboard_activity}ms ({keyboard_activity/1000:.1f}s)")
        print(f"Mouse Inactive:      {mouse_activity}ms ({mouse_activity/1000:.1f}s)")
        print(f"Attention Span:      {time_last_request}ms ({time_last_request/1000:.1f}s)")
        print("-"*50)
        print(f"Prediction:          {'PRODUCTIVE ✓' if productive else 'NOT PRODUCTIVE ✗'}")
        print(f"Probability:         {prob:.1%}")
        print(f"Confidence:          {conf:.1%}")
        print("="*50 + "\n")

        # Provide insights
        if not productive:
            print("💡 Possible reasons:")
            if keyboard_activity > 30000 and mouse_activity > 30000:
                print("   - Both keyboard and mouse have been inactive for >30s")
            if time_last_request < 5000:
                print("   - Very short attention span (app switching)")
            if time_last_request > 300000:
                print("   - Been in same app for >5 minutes without activity")

        return prob, productive, conf

def main():
    """Test the predictor with sample inputs"""

    if len(sys.argv) == 5:
        # Command line arguments provided
        foreground_app = int(sys.argv[1])
        keyboard_activity = int(sys.argv[2])
        mouse_activity = int(sys.argv[3])
        time_last_request = int(sys.argv[4])

        predictor = ProductivityPredictor()
        predictor.explain_prediction(
            foreground_app, keyboard_activity, mouse_activity, time_last_request
        )

    else:
        # Interactive mode
        print("=== Nudge Productivity Predictor ===\n")

        try:
            predictor = ProductivityPredictor()
        except FileNotFoundError as e:
            print(f"❌ {e}")
            print("\nTrain a model first using: python train_model.py /tmp/HARVEST.CSV")
            sys.exit(1)

        print("\nEnter activity data to predict productivity:")
        print("(Press Ctrl+C to exit)\n")

        while True:
            try:
                print("Enter values:")
                foreground_app = int(input("  Foreground app hash: "))
                keyboard_activity = int(input("  Keyboard inactive (ms): "))
                mouse_activity = int(input("  Mouse inactive (ms): "))
                time_last_request = int(input("  Attention span (ms): "))

                predictor.explain_prediction(
                    foreground_app, keyboard_activity, mouse_activity, time_last_request
                )

            except KeyboardInterrupt:
                print("\n\nGoodbye!")
                break
            except ValueError:
                print("❌ Invalid input. Please enter integers only.\n")

if __name__ == '__main__':
    main()
