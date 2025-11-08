#!/usr/bin/env python3
"""
Validate that the CSV data collected by NudgeHarvester matches the expected format
for the ML model training.
"""

import sys
import csv
import os

def validate_csv(csv_path):
    """Validate the CSV file format"""

    if not os.path.exists(csv_path):
        print(f"❌ File not found: {csv_path}")
        return False

    # Expected columns in exact order
    expected_columns = [
        'foreground_app',
        'keyboard_activity',
        'mouse_activity',
        'time_last_request',
        'productive'
    ]

    try:
        with open(csv_path, 'r') as f:
            reader = csv.DictReader(f)

            # Check header
            if reader.fieldnames != expected_columns:
                print("❌ Column names don't match!")
                print(f"Expected: {expected_columns}")
                print(f"Got:      {list(reader.fieldnames)}")
                return False

            print("✓ Column names match")

            # Check data types and ranges
            row_count = 0
            for i, row in enumerate(reader, 1):
                row_count = i

                # Validate each field
                try:
                    foreground_app = int(row['foreground_app'])
                    keyboard_activity = int(row['keyboard_activity'])
                    mouse_activity = int(row['mouse_activity'])
                    time_last_request = int(row['time_last_request'])
                    productive = int(row['productive'])

                    # Check productive is binary
                    if productive not in [0, 1]:
                        print(f"❌ Row {i}: productive must be 0 or 1, got {productive}")
                        return False

                    # Check for negative values (except hash)
                    if keyboard_activity < 0 or mouse_activity < 0 or time_last_request < 0:
                        print(f"❌ Row {i}: activity values cannot be negative")
                        return False

                except ValueError as e:
                    print(f"❌ Row {i}: Invalid data type - {e}")
                    return False

            print(f"✓ All {row_count} rows have valid data types")
            print(f"✓ All productivity labels are binary (0 or 1)")

            # Check for sufficient data
            if row_count < 10:
                print(f"⚠️  Warning: Only {row_count} rows found. You need more data for training.")
            else:
                print(f"✓ Found {row_count} data points")

            # Calculate class balance
            with open(csv_path, 'r') as f:
                reader = csv.DictReader(f)
                productive_count = sum(1 for row in reader if row['productive'] == '1')
                unproductive_count = row_count - productive_count

                print(f"\n📊 Data Statistics:")
                print(f"   Productive:     {productive_count} ({productive_count/row_count*100:.1f}%)")
                print(f"   Not Productive: {unproductive_count} ({unproductive_count/row_count*100:.1f}%)")

                if productive_count == 0 or unproductive_count == 0:
                    print(f"⚠️  Warning: Need examples of BOTH productive and unproductive behavior")
                elif abs(productive_count - unproductive_count) / row_count > 0.8:
                    print(f"⚠️  Warning: Data is very imbalanced. Try to label both types more evenly.")
                else:
                    print(f"✓ Data has reasonable class balance")

            return True

    except Exception as e:
        print(f"❌ Error reading file: {e}")
        return False

if __name__ == '__main__':
    csv_path = sys.argv[1] if len(sys.argv) > 1 else '/tmp/HARVEST.CSV'

    print(f"Validating: {csv_path}\n")

    if validate_csv(csv_path):
        print("\n✅ CSV format is correct and ready for model training!")
        sys.exit(0)
    else:
        print("\n❌ CSV validation failed. Please fix the issues above.")
        sys.exit(1)
