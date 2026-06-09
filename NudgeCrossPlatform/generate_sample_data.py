#!/usr/bin/env python3
"""
Generate synthetic productivity data for model pre-training.

Supports both V3 and V4 schema.

Usage:
  python generate_sample_data.py                          # default: 500 V3 samples to /tmp/HARVEST.CSV
  python generate_sample_data.py --samples 1000 --output ~/.nudge/HARVEST.CSV
  python generate_sample_data.py --schema v4 --samples 600 --output ~/.nudge/HARVEST_EXP.CSV
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

# Full V3 harvest schema header
HARVEST_HEADER_V3 = [
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

# V4 harvest schema header (drops category flags, adds OS signals + personalization)
HARVEST_HEADER_V4 = [
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
    'afk_flag', 'workspace_switch_count_300s',
    'audio_playing_flag', 'media_session_active_flag', 'mic_active_flag',
    'domain_productive_rate', 'domain_label_count',
    'app_productive_rate', 'app_label_count',
]


# Known domains with pre-learned reputation for V4 seed data
KNOWN_DOMAINS = {
    'github.com':          (0.90, 42),   # very productive, many labels
    'stackoverflow.com':   (0.85, 38),
    'docs.python.org':     (0.88, 25),
    'notion.so':           (0.78, 30),
    'gmail.com':           (0.60, 20),
    'reddit.com':          (0.12, 35),   # unproductive
    'youtube.com':         (0.08, 50),
    'netflix.com':         (0.05, 28),
    'twitter.com':         (0.15, 32),
    'discord.com':         (0.25, 18),
    'slack.com':           (0.45, 22),
    'zoom.us':             (0.30, 15),
}

KNOWN_APPS = {
    'code':        (0.92, 45),
    'vim':         (0.90, 30),
    'idea':        (0.88, 28),
    'gimp':        (0.82, 20),
    'blender':     (0.80, 18),
    'firefox':     (0.45, 35),
    'chrome':      (0.42, 40),
    'mpv':         (0.10, 15),
    'spotify':     (0.12, 20),
    'slack':       (0.35, 18),
    'discord':     (0.20, 16),
    'teams':       (0.30, 14),
    'zoom':        (0.28, 12),
}


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


def _pick_domain(category, browser_flag):
    """Return a domain name and its reputation for the given category."""
    if not browser_flag:
        return '', 0.5, 0

    # Filter known domains by approximate category alignment
    candidates = []
    for domain, (rate, count) in KNOWN_DOMAINS.items():
        if category == 'development' and rate > 0.7:
            candidates.append((domain, rate, count))
        elif category == 'entertainment' and rate < 0.3:
            candidates.append((domain, rate, count))
        elif category == 'communication' and 0.2 < rate < 0.6:
            candidates.append((domain, rate, count))
        elif category == 'unknown' and 0.3 < rate < 0.7:
            candidates.append((domain, rate, count))
        elif category in ('office', 'creative', 'utility'):
            candidates.append((domain, rate, count))

    if candidates and random.random() < 0.75:
        return _maybe_prior_only(random.choice(candidates))

    # Unknown domain — neutral reputation
    return '', 0.5, 0


def _maybe_prior_only(picked):
    """Half the reputation-bearing samples carry shipped-DKB evidence only:
    label count 0 with a rate drawn from the prior table's distribution
    (distraction ≈ 0.05-0.2, productive ≈ 0.8-0.95)."""
    name, rate, count = picked
    if random.random() < 0.5:
        if rate < 0.3:
            rate = round(random.uniform(0.05, 0.2), 3)
        elif rate > 0.7:
            rate = round(random.uniform(0.8, 0.95), 3)
        return name, rate, 0
    return name, rate, count


def _pick_app(category):
    """Return an app name and its reputation for the given category."""
    candidates = []
    for app, (rate, count) in KNOWN_APPS.items():
        if category == 'development' and rate > 0.7:
            candidates.append((app, rate, count))
        elif category == 'entertainment' and rate < 0.3:
            candidates.append((app, rate, count))
        elif category == 'communication' and rate < 0.4:
            candidates.append((app, rate, count))
        elif category in ('office', 'creative', 'utility', 'unknown'):
            candidates.append((app, rate, count))

    if candidates and random.random() < 0.75:
        return _maybe_prior_only(random.choice(candidates))

    # Unknown app — neutral reputation
    return '', 0.5, 0


def _v4_signals(category, browser_flag, app_share, focused_since_ms, switch_300s):
    """Generate realistic V4 OS-sensor signals correlated with the scenario.

    Signals are designed to be strongly discriminative for the V4 model:
    - entertainment + browser + audio + media + fullscreen → unproductive
    - communication + mic → unproductive (calls/meetings are not "productive work")
    - development/office + low switching + high app_share + long focus → productive
    """
    audio = 0
    media = 0
    mic = 0
    fullscreen = 0

    if category == 'entertainment' and browser_flag:
        # Passive video / music → very high chance of audio + media session + fullscreen
        audio = 1 if random.random() < 0.90 else 0
        media = 1 if random.random() < 0.80 else 0
        fullscreen = 1 if random.random() < 0.70 else 0
    elif category == 'communication':
        # Video/voice call → mic active, sometimes audio
        mic = 1 if random.random() < 0.70 else 0
        audio = 1 if mic or random.random() < 0.30 else 0
    elif category in ('development', 'office', 'creative'):
        # Deep work → usually quiet; occasional background music
        if random.random() < 0.15:
            audio = 1
            media = 1 if random.random() < 0.60 else 0

    return audio, media, mic, fullscreen


def generate_sample_data(num_samples=500, output_file='/tmp/HARVEST.CSV', schema='v3'):
    """Generate synthetic productivity data for model pre-training.

    Supports V3 (legacy category-flag schema) and V4 (signal-based schema).

    Args:
        num_samples: Number of synthetic rows to generate (default: 500).
        output_file: Path for the output CSV (default: /tmp/HARVEST.CSV).
        schema: 'v3' or 'v4' (default: v3).

    Returns:
        The output_file path on success.
    """
    if schema not in ('v3', 'v4'):
        raise ValueError(f"schema must be 'v3' or 'v4', got {schema}")

    is_v4 = schema == 'v4'
    categories = list(BASE_CATEGORY_WEIGHTS.keys())
    weights = list(BASE_CATEGORY_WEIGHTS.values())

    # Spread timestamps across a 14-day window for realistic hour/day coverage
    now = time_mod.time()
    window_start = now - 14 * 86400

    print(f'Generating {num_samples} {schema.upper()}-schema samples across 14-day window...')

    prod_count = 0
    unprod_count = 0

    header = HARVEST_HEADER_V4 if is_v4 else HARVEST_HEADER_V3

    with open(output_file, 'w', newline='') as csvfile:
        writer = csv.writer(csvfile)
        writer.writerow(header)

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
            ws_switches = _randint(feat['workspace_switches'])

            # Quiet distraction (V4): scrolling one feed is behaviorally identical
            # to reading docs — stable focus, one domain, no media. Only the DKB
            # reputation separates it, so the model must learn to act on the prior.
            quiet_distraction = is_v4 and category == 'entertainment' and random.random() < 0.45
            if quiet_distraction:
                idle_ms = random.randint(200, 5000)
                focused_since_ms = random.randint(60000, 480000)
                title_stability_ms = random.randint(30000, 240000)
                switch_60s = random.randint(0, 2)
                switch_300s = random.randint(0, 6)
                distinct_apps = random.randint(1, 2)
                app_share = round(random.uniform(0.6, 0.98), 4)
                browser_flag = 1
                ws_switches = 0

            # Domain features
            domain_hash = random.randint(0, 100000) if browser_flag else 0
            domain_share = round(app_share * random.uniform(0.5, 1.0), 4) if browser_flag else 0.0
            distinct_domains = random.randint(0, min(4, distinct_apps)) if browser_flag else 0

            # Anchor return — more common in focused/productive sessions
            returned_to_anchor = 1 if random.random() < (0.35 if bias > 0.5 else 0.12) else 0

            # ── Label determination ──
            if is_v4:
                # V4: stronger signal-driven label with reputation + OS sensors
                domain, domain_rate, domain_count = _pick_domain(category, browser_flag)
                app_name, app_rate, app_count = _pick_app(category)
                audio, media, mic, fullscreen = _v4_signals(
                    category, browser_flag, app_share, focused_since_ms, switch_300s)
                if quiet_distraction:
                    audio = media = mic = fullscreen = 0
                fullscreen_flag = fullscreen

                noise = random.gauss(0, 0.06)
                base_bias = max(0.05, min(0.95, bias + noise))

                signal_adj = 0.0
                if audio and media and fullscreen:
                    signal_adj -= 0.35
                elif audio and media:
                    signal_adj -= 0.20
                elif mic:
                    signal_adj -= 0.15

                if switch_300s > 18:
                    signal_adj -= 0.12
                elif switch_300s < 4:
                    signal_adj += 0.10
                if app_share > 0.75:
                    signal_adj += 0.10
                elif app_share < 0.25:
                    signal_adj -= 0.08
                if focused_since_ms > 180000:
                    signal_adj += 0.08
                if title_stability_ms > 120000:
                    signal_adj += 0.06

                # Reputation coupling — no label-count gate: the shipped DKB prior
                # arrives with count=0 and the model must act on prior-only evidence.
                signal_adj += (domain_rate - 0.5) * 0.5
                signal_adj += (app_rate - 0.5) * 0.4

                effective_bias = max(0.02, min(0.98, base_bias + signal_adj))
                productive = 1 if random.random() < effective_bias else 0
            else:
                # V3: original category-bias + behavioral noise
                noise = random.gauss(0, 0.10)
                adjusted_bias = max(0.02, min(0.98, bias + noise))

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
                fullscreen_flag = 1 if random.random() < feat['fullscreen_prob'] else 0

            if productive:
                prod_count += 1
            else:
                unprod_count += 1

            if is_v4:
                writer.writerow([
                    ts,
                    hour,
                    day_of_week,
                    app_name,
                    '',
                    idle_ms,
                    focused_since_ms,
                    productive,
                    4,  # schema_version for V4
                    app_name,
                    '',
                    domain,
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
                    0,  # afk_flag
                    ws_switches,
                    audio,
                    media,
                    mic,
                    round(domain_rate, 4),
                    domain_count,
                    round(app_rate, 4),
                    app_count,
                ])
            else:
                # V3 path with category flags
                comm_domain_flag, ent_domain_flag, work_domain_flag = DOMAIN_FLAGS[category]
                dev_flag, creative_flag, office_flag, comm_app_flag, ent_app_flag = CATEGORY_FLAGS[category]

                writer.writerow([
                    ts,
                    hour,
                    day_of_week,
                    '',
                    '',
                    idle_ms,
                    focused_since_ms,
                    productive,
                    3,  # schema_version for V3
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
        description='Generate synthetic productivity data for model pre-training')
    parser.add_argument('--samples', type=int, default=500,
                        help='Number of samples (default: 500)')
    parser.add_argument('--output', default='/tmp/HARVEST.CSV',
                        help='Output CSV path (default: /tmp/HARVEST.CSV)')
    parser.add_argument('--schema', default='v3', choices=['v3', 'v4'],
                        help='Schema version: v3 (category flags) or v4 (signal-based)')
    args = parser.parse_args()
    generate_sample_data(args.samples, args.output, args.schema)
