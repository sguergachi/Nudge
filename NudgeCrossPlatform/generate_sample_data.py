#!/usr/bin/env python3
"""
Generate V3-schema synthetic productivity data for model pre-training.

Creates labeled training data with realistic feature distributions based on
app categories, time-of-day, and work/eve/weekend patterns. The synthetic data
gives the model a useful prior before real user labels accumulate.

Usage:
  python generate_sample_data.py                          # default: 500 samples to /tmp/HARVEST.CSV
  python generate_sample_data.py --samples 1000 --output ~/.nudge/HARVEST.CSV
  python generate_sample_data.py --output ~/.nudge/seed_data.csv
"""

import random
import csv
import os
import argparse
import time as time_mod


# Probability of productive label per app category
CATEGORY_BIAS = {
    'development':   0.92,
    'creative':      0.85,
    'office':        0.82,
    'communication': 0.25,
    'entertainment': 0.08,
    'utility':       0.55,
    'unknown':       0.40,
}

# Feature ranges per category: (low, high) for random generation
# Each tuple is (min, max) — values drawn uniformly from this range
CATEGORY_FEATURES = {
    'development': {
        'idle_ms': (100, 8000),
        'focused_since_ms': (60000, 600000),
        'title_stability_ms': (30000, 300000),
        'switch_count_60s': (0, 3),
        'switch_count_300s': (2, 10),
        'distinct_apps_300s': (1, 3),
        'app_share_300s': (0.40, 0.90),
        'browser_prob': 0.10,
        'fullscreen_prob': 0.20,
        'workspace_switches': (0, 2),
    },
    'creative': {
        'idle_ms': (200, 15000),
        'focused_since_ms': (30000, 300000),
        'title_stability_ms': (15000, 200000),
        'switch_count_60s': (1, 5),
        'switch_count_300s': (3, 15),
        'distinct_apps_300s': (2, 5),
        'app_share_300s': (0.30, 0.80),
        'browser_prob': 0.30,
        'fullscreen_prob': 0.40,
        'workspace_switches': (0, 3),
    },
    'office': {
        'idle_ms': (50, 5000),
        'focused_since_ms': (120000, 900000),
        'title_stability_ms': (60000, 400000),
        'switch_count_60s': (0, 2),
        'switch_count_300s': (1, 8),
        'distinct_apps_300s': (1, 3),
        'app_share_300s': (0.50, 0.95),
        'browser_prob': 0.50,
        'fullscreen_prob': 0.05,
        'workspace_switches': (0, 1),
    },
    'communication': {
        'idle_ms': (200, 12000),
        'focused_since_ms': (10000, 120000),
        'title_stability_ms': (5000, 60000),
        'switch_count_60s': (2, 8),
        'switch_count_300s': (8, 25),
        'distinct_apps_300s': (3, 7),
        'app_share_300s': (0.10, 0.40),
        'browser_prob': 0.70,
        'fullscreen_prob': 0.02,
        'workspace_switches': (0, 2),
    },
    'entertainment': {
        'idle_ms': (1000, 30000),
        'focused_since_ms': (5000, 90000),
        'title_stability_ms': (3000, 45000),
        'switch_count_60s': (3, 12),
        'switch_count_300s': (10, 35),
        'distinct_apps_300s': (4, 8),
        'app_share_300s': (0.05, 0.30),
        'browser_prob': 0.85,
        'fullscreen_prob': 0.15,
        'workspace_switches': (0, 1),
    },
    'utility': {
        'idle_ms': (500, 10000),
        'focused_since_ms': (15000, 180000),
        'title_stability_ms': (10000, 120000),
        'switch_count_60s': (1, 5),
        'switch_count_300s': (4, 15),
        'distinct_apps_300s': (2, 5),
        'app_share_300s': (0.20, 0.60),
        'browser_prob': 0.30,
        'fullscreen_prob': 0.05,
        'workspace_switches': (0, 1),
    },
    'unknown': {
        'idle_ms': (200, 10000),
        'focused_since_ms': (10000, 150000),
        'title_stability_ms': (5000, 100000),
        'switch_count_60s': (1, 6),
        'switch_count_300s': (5, 20),
        'distinct_apps_300s': (2, 6),
        'app_share_300s': (0.15, 0.50),
        'browser_prob': 0.40,
        'fullscreen_prob': 0.05,
        'workspace_switches': (0, 2),
    },
}

# Category weight distribution — adjusted by time-of-day during generation
BASE_CATEGORY_WEIGHTS = {
    'development': 0.30,
    'creative':    0.08,
    'office':      0.15,
    'communication': 0.15,
    'entertainment': 0.12,
    'utility':     0.12,
    'unknown':     0.08,
}

# (dev_app, creative_app, office_app, comm_app, ent_app) flags per category
CATEGORY_FLAGS = {
    'development':   (1, 0, 0, 0, 0),
    'creative':      (0, 1, 0, 0, 0),
    'office':        (0, 0, 1, 0, 0),
    'communication': (0, 0, 0, 1, 0),
    'entertainment': (0, 0, 0, 0, 1),
    'utility':       (0, 0, 0, 0, 0),
    'unknown':       (0, 0, 0, 0, 0),
}

# (communication_app_flag, entertainment_domain_flag, work_domain_flag)
DOMAIN_FLAGS = {
    'development':   (0, 0, 1),
    'creative':      (0, 0, 0),
    'office':        (0, 0, 1),
    'communication': (1, 0, 0),
    'entertainment': (0, 1, 0),
    'utility':       (0, 0, 0),
    'unknown':       (0, 0, 0),
}

# Full V3 harvest schema header (matching FeatureSchema.HarvestHeaders)
HARVEST_HEADER = [
    'timestamp', 'hour_of_day', 'day_of_week', 'app_name', 'foreground_app',
    'idle_time', 'time_last_request', 'productive', 'schema_version',
    'focused_app_id', 'focused_title', 'focused_domain', 'focused_window_id',
    'is_idle_now', 'focused_since_ms', 'title_unchanged_for_ms',
    'mapped_toplevel_count', 'active_workspace_id', 'focus_source',
    'signal_quality', 'fullscreen_flag',
    'focused_app_hash', 'focused_domain_hash', 'idle_ms',
    'title_stability_ms', 'switch_count_60s', 'switch_count_300s',
    'distinct_apps_300s', 'distinct_domains_300s',
    'returned_to_anchor_app_300s', 'current_app_share_300s',
    'current_domain_share_300s', 'browser_window_flag',
    'communication_app_flag', 'entertainment_domain_flag', 'work_domain_flag',
    'afk_flag', 'workspace_switch_count_300s',
    'dev_app_flag', 'creative_app_flag', 'office_app_flag', 'comm_app_flag',
    'ent_app_flag',
]


def _randint(r):
    return random.randint(r[0], r[1])


def _randfloat(r):
    return round(random.uniform(r[0], r[1]), 4)


def _adjust_weights(categories, weights, hour, is_weekend):
    """Scale category weights by time-of-day for realistic session patterns."""
    adjusted = list(weights)
    work_hours = 8 <= hour <= 17
    evening = 18 <= hour <= 23

    for idx, cat in enumerate(categories):
        if work_hours and not is_weekend:
            if cat in ('development', 'office'):
                adjusted[idx] *= 1.8
            elif cat in ('entertainment',):
                adjusted[idx] *= 0.3
            elif cat in ('communication',):
                adjusted[idx] *= 1.3
        elif evening or is_weekend:
            if cat in ('development', 'office'):
                adjusted[idx] *= 0.5
            elif cat in ('entertainment',):
                adjusted[idx] *= 2.5
            elif cat in ('communication',):
                adjusted[idx] *= 1.2

    total = sum(adjusted)
    return [w / total for w in adjusted]


def generate_sample_data(num_samples=500, output_file='/tmp/HARVEST.CSV'):
    """Generate synthetic V3-schema productivity data for model pre-training.

    Produces labeled data with realistic feature distributions conditioned on
    app categories, time-of-day, and work/eve/weekend patterns. The data is
    schema-compatible with train_model.py's V3 feature pipeline.

    Args:
        num_samples: Number of synthetic rows to generate (default: 500).
        output_file: Path for the output CSV (default: /tmp/HARVEST.CSV).

    Returns:
        The output_file path on success.
    """
    categories = list(BASE_CATEGORY_WEIGHTS.keys())
    weights = list(BASE_CATEGORY_WEIGHTS.values())

    # Spread timestamps across a 14-day window for realistic hour/day coverage
    now = time_mod.time()
    window_start = now - 14 * 86400

    print(f'Generating {num_samples} V3-schema samples across 14-day window...')

    prod_count = 0
    unprod_count = 0

    with open(output_file, 'w', newline='') as csvfile:
        writer = csv.writer(csvfile)
        writer.writerow(HARVEST_HEADER)

        for i in range(num_samples):
            # Random timestamp across the 14-day window
            ts = int(random.uniform(window_start, now))
            dt = time_mod.localtime(ts)
            hour = dt.tm_hour
            day_of_week = dt.tm_wday  # 0=Mon, 6=Sun
            is_weekend = day_of_week >= 5

            # Pick category with time-of-day-adjusted weights
            adj_weights = _adjust_weights(categories, weights, hour, is_weekend)
            category = random.choices(categories, weights=adj_weights, k=1)[0]

            bias = CATEGORY_BIAS[category]
            feat = CATEGORY_FEATURES[category]

            # Generate core feature values
            idle_ms = _randint(feat['idle_ms'])
            focused_since_ms = _randint(feat['focused_since_ms'])
            title_stability_ms = _randint(feat['title_stability_ms'])
            switch_60s = _randint(feat['switch_count_60s'])
            switch_300s = _randint(feat['switch_count_300s'])
            distinct_apps = _randint(feat['distinct_apps_300s'])
            app_share = _randfloat(feat['app_share_300s'])
            browser_flag = 1 if random.random() < feat['browser_prob'] else 0
            fullscreen_flag = 1 if random.random() < feat['fullscreen_prob'] else 0
            ws_switches = _randint(feat['workspace_switches'])

            # Domain features (only meaningful when browser is focused)
            domain_hash = random.randint(0, 100000) if browser_flag else 0
            domain_share = round(app_share * random.uniform(0.5, 1.0), 4) if browser_flag else 0.0
            distinct_domains = random.randint(0, min(4, distinct_apps)) if browser_flag else 0

            # Anchor return — more common in focused/productive sessions
            returned_to_anchor = 1 if random.random() < (0.35 if bias > 0.5 else 0.12) else 0

            # Domain-level flags
            comm_domain_flag, ent_domain_flag, work_domain_flag = DOMAIN_FLAGS[category]

            # App-category flags
            dev_flag, creative_flag, office_flag, comm_app_flag, ent_app_flag = CATEGORY_FLAGS[category]

            # Label: category bias + noise, then flip based on behavioral signals
            noise = random.gauss(0, 0.10)
            adjusted_bias = max(0.02, min(0.98, bias + noise))

            # Secondary behavioral adjustment: within-category, feature values
            # affect the likelihood (e.g., high idle → more unproductive)
            behavior_signal = 0.0
            if idle_ms > 15000:
                behavior_signal -= 0.10
            elif idle_ms < 1000:
                behavior_signal += 0.05
            if switch_300s > 20:
                behavior_signal -= 0.08
            elif switch_300s < 5:
                behavior_signal += 0.05
            if app_share > 0.7:
                behavior_signal += 0.08
            if returned_to_anchor:
                behavior_signal += 0.05

            effective_bias = max(0.02, min(0.98, adjusted_bias + behavior_signal))
            productive = 1 if random.random() < effective_bias else 0

            if productive:
                prod_count += 1
            else:
                unprod_count += 1

            writer.writerow([
                ts,
                hour,
                day_of_week,
                '',
                '',
                idle_ms,
                focused_since_ms,
                productive,
                3,
                '',
                '',
                '',
                '',
                0,
                focused_since_ms,
                title_stability_ms,
                random.randint(1, 5),
                '',
                'ewmh',
                'good',
                fullscreen_flag,
                random.randint(0, 100000),
                domain_hash,
                idle_ms,
                title_stability_ms,
                switch_60s,
                switch_300s,
                distinct_apps,
                distinct_domains,
                returned_to_anchor,
                app_share,
                domain_share,
                browser_flag,
                comm_domain_flag,
                ent_domain_flag,
                work_domain_flag,
                0,  # afk_flag
                ws_switches,
                dev_flag,
                creative_flag,
                office_flag,
                comm_app_flag,
                ent_app_flag,
            ])

            if (i + 1) % 100 == 0:
                print(f'  {i + 1}/{num_samples}')

    total = prod_count + unprod_count
    print(f'\nDataset: {total} rows')
    print(f'  Productive:   {prod_count} ({prod_count / total * 100:.1f}%)')
    print(f'  Unproductive: {unprod_count} ({unprod_count / total * 100:.1f}%)')

    return output_file


if __name__ == '__main__':
    parser = argparse.ArgumentParser(
        description='Generate V3-schema synthetic productivity data')
    parser.add_argument('--samples', type=int, default=500,
                        help='Number of samples (default: 500)')
    parser.add_argument('--output', default='/tmp/HARVEST.CSV',
                        help='Output CSV path (default: /tmp/HARVEST.CSV)')
    args = parser.parse_args()
    generate_sample_data(args.samples, args.output)
