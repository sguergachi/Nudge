#!/usr/bin/env python3
"""
Generate sample productivity data for testing ML features.

This creates synthetic training data that simulates realistic productivity patterns.
"""

import random
import csv

def generate_sample_data(num_samples=150, output_file='/tmp/HARVEST.CSV'):
    """Generate synthetic productivity data"""

    # Simulate different app patterns
    productive_apps = [
        ('vscode', 0.9),      # High productivity
        ('terminal', 0.85),
        ('firefox-dev', 0.8),
        ('intellij', 0.9),
        ('sublime', 0.85),
    ]

    unproductive_apps = [
        ('slack', 0.3),       # Low productivity
        ('youtube', 0.1),
        ('twitter', 0.2),
        ('reddit', 0.15),
        ('netflix', 0.05),
    ]

    mixed_apps = [
        ('firefox', 0.5),     # Could go either way
        ('chrome', 0.5),
        ('email', 0.4),
    ]

    all_apps = productive_apps + unproductive_apps + mixed_apps

    print(f"Generating {num_samples} sample records...")

    with open(output_file, 'w', newline='') as csvfile:
        writer = csv.writer(csvfile)
        # Write header
        writer.writerow(['foreground_app', 'idle_time', 'time_last_request', 'productive'])

        for i in range(num_samples):
            # Pick an app with weighted randomness
            app_name, productivity_bias = random.choice(all_apps)

            # Hash the app name (simple hash for demo)
            app_hash = hash(app_name) % 100000

            # Generate idle time (productive work = lower idle time usually)
            if random.random() < productivity_bias:
                # Productive: lower idle time
                idle_time = random.randint(0, 5000)
                # Productive: longer attention span
                attention_span = random.randint(30000, 600000)  # 30s to 10min
                productive = 1
            else:
                # Unproductive: higher idle time
                idle_time = random.randint(2000, 15000)
                # Unproductive: shorter attention span
                attention_span = random.randint(5000, 120000)  # 5s to 2min
                productive = 0

            writer.writerow([app_hash, idle_time, attention_span, productive])

            if (i + 1) % 50 == 0:
                print(f"  Generated {i + 1}/{num_samples} records...")

    print(f"✅ Created {output_file} with {num_samples} records")

    # Show statistics
    with open(output_file, 'r') as f:
        lines = list(csv.reader(f))[1:]  # Skip header
        productive_count = sum(1 for line in lines if line[3] == '1')
        unproductive_count = len(lines) - productive_count

        print(f"\n📊 Dataset Statistics:")
        print(f"   Productive: {productive_count} ({productive_count/len(lines)*100:.1f}%)")
        print(f"   Unproductive: {unproductive_count} ({unproductive_count/len(lines)*100:.1f}%)")

if __name__ == '__main__':
    import argparse

    parser = argparse.ArgumentParser(description='Generate sample productivity data')
    parser.add_argument('--samples', type=int, default=150, help='Number of samples to generate')
    parser.add_argument('--output', default='/tmp/HARVEST.CSV', help='Output CSV file')

    args = parser.parse_args()

    generate_sample_data(args.samples, args.output)
