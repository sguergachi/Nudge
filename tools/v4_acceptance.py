#!/usr/bin/env python3
"""
V4 seed-model acceptance check (PRETRAINED_DISTRACTION_MODEL.md §5).

Verifies the shipped experimental seed acts on the distraction prior:
  - quiet x.com scrolling  → NOT productive, confidence ≥ trigger threshold (fires)
  - VS Code deep work      → productive (never fires)
  - quiet docs reading     → does not fire
  - quiet unknown site     → does not fire

Default: loads the bundled model in-process from NudgeCrossPlatform/model_exp.
  python tools/v4_acceptance.py
  python tools/v4_acceptance.py --model-dir ~/.nudge/model_exp
  python tools/v4_acceptance.py --server --port 45003   # exercise the TCP wire path
  python tools/v4_acceptance.py --sweep 500             # trigger-rate sweep over sampled rows

Exit code 0 = all scenarios pass, 1 = any failure.
"""

import argparse
import json
import os
import random
import socket
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP_DIR = os.path.join(REPO_ROOT, 'NudgeCrossPlatform')
sys.path.insert(0, APP_DIR)

# Experimental-mode ML trigger threshold (nudge.cs ML_CONFIDENCE_THRESHOLD, V4 = 0.75)
TRIGGER_THRESHOLD = 0.75

FEATURE_ORDER = [
    'hour_of_day', 'day_of_week', 'focused_app_hash', 'focused_domain_hash',
    'idle_ms', 'focused_since_ms', 'title_stability_ms',
    'switch_count_60s', 'switch_count_300s', 'distinct_apps_300s',
    'distinct_domains_300s', 'returned_to_anchor_app_300s',
    'current_app_share_300s', 'current_domain_share_300s',
    'browser_window_flag', 'afk_flag', 'fullscreen_flag',
    'workspace_switch_count_300s', 'audio_playing_flag',
    'media_session_active_flag', 'mic_active_flag',
    'domain_productive_rate', 'domain_label_count',
    'app_productive_rate', 'app_label_count',
]


def quiet_browsing_row(domain_rate, rng=None):
    """A 'quietly reading one site in a browser' row — stable focus, one domain,
    no media. Mirrors the quiet_distraction block in generate_sample_data.py;
    only the reputation prior separates scrolling x.com from reading docs."""
    r = rng or random
    return {
        'hour_of_day': 14, 'day_of_week': 2,
        'focused_app_hash': r.randint(0, 100000) if rng else 50000,
        'focused_domain_hash': r.randint(0, 100000) if rng else 50000,
        'idle_ms': r.randint(200, 5000) if rng else 2000,
        'focused_since_ms': r.randint(60000, 480000) if rng else 240000,
        'title_stability_ms': r.randint(30000, 240000) if rng else 120000,
        'switch_count_60s': r.randint(0, 2) if rng else 1,
        'switch_count_300s': r.randint(0, 6) if rng else 3,
        'distinct_apps_300s': r.randint(1, 2) if rng else 1,
        'distinct_domains_300s': 1,
        'returned_to_anchor_app_300s': 0,
        'current_app_share_300s': round(r.uniform(0.6, 0.98), 4) if rng else 0.85,
        'current_domain_share_300s': round((r.uniform(0.6, 0.98) * r.uniform(0.5, 1.0)) if rng else 0.70, 4),
        'browser_window_flag': 1, 'afk_flag': 0, 'fullscreen_flag': 0,
        'workspace_switch_count_300s': 0,
        'audio_playing_flag': 0, 'media_session_active_flag': 0, 'mic_active_flag': 0,
        'domain_productive_rate': domain_rate, 'domain_label_count': 0,
        'app_productive_rate': 0.5, 'app_label_count': 0,  # browser app: no prior
    }


def deep_work_row():
    """VS Code deep work: long stable focus, no browser, strong app prior (code = 0.90)."""
    return {
        'hour_of_day': 14, 'day_of_week': 2,
        'focused_app_hash': 50000, 'focused_domain_hash': 0,
        'idle_ms': 1500, 'focused_since_ms': 420000, 'title_stability_ms': 180000,
        'switch_count_60s': 0, 'switch_count_300s': 3,
        'distinct_apps_300s': 2, 'distinct_domains_300s': 0,
        'returned_to_anchor_app_300s': 1,
        'current_app_share_300s': 0.85, 'current_domain_share_300s': 0.0,
        'browser_window_flag': 0, 'afk_flag': 0, 'fullscreen_flag': 0,
        'workspace_switch_count_300s': 1,
        'audio_playing_flag': 0, 'media_session_active_flag': 0, 'mic_active_flag': 0,
        'domain_productive_rate': 0.5, 'domain_label_count': 0,
        'app_productive_rate': 0.90, 'app_label_count': 0,
    }


def passive_video_row(domain_rate):
    """Fullscreen media with long stable focus — passive consumption. Must fire
    even on an unknown domain (EXPERIMENTAL_SIGNAL_MODE.md known-limitation fix)."""
    row = quiet_browsing_row(domain_rate)
    row.update(audio_playing_flag=1, media_session_active_flag=1, fullscreen_flag=1,
               idle_ms=8000, focused_since_ms=480000, title_stability_ms=300000,
               switch_count_60s=0, switch_count_300s=1,
               current_app_share_300s=0.95, current_domain_share_300s=0.9)
    return row


def deep_work_music_row():
    """VS Code deep work with background music — media signals must not outweigh
    the strong app reputation."""
    row = deep_work_row()
    row.update(audio_playing_flag=1, media_session_active_flag=1)
    return row


def overridden_distraction_row():
    """x.com relabeled productive by the user (12 YES labels) — personalization
    must beat the shipped prior."""
    row = quiet_browsing_row(0.85)
    row['domain_label_count'] = 12
    return row


def gaming_row(app_rate):
    """Fullscreen gaming: stable desktop focus, audio, no media session. The
    shipped app prior (steam = 0.10) is the only distraction evidence."""
    row = deep_work_row()
    row.update(app_productive_rate=app_rate, fullscreen_flag=1, audio_playing_flag=1,
               focused_since_ms=600000, title_stability_ms=300000,
               switch_count_300s=1, current_app_share_300s=0.95,
               returned_to_anchor_app_300s=0, workspace_switch_count_300s=0)
    return row


SCENARIOS = [
    # (name, row, expectation)  expectation: 'fires' | 'no_fire' | 'productive'
    ('quiet x.com scrolling (prior 0.10)', quiet_browsing_row(0.10), 'fires'),
    ('VS Code deep work (app prior 0.90)', deep_work_row(), 'productive'),
    ('quiet docs reading (prior 0.90)', quiet_browsing_row(0.90), 'productive'),
    ('quiet unknown site (prior 0.50)', quiet_browsing_row(0.50), 'no_fire'),
    ('passive video, unknown domain (0.50)', passive_video_row(0.50), 'fires'),
    ('passive video, youtube-like (0.08)', passive_video_row(0.08), 'fires'),
    ('deep work + background music', deep_work_music_row(), 'productive'),
    ('x.com after 12 user YES labels', overridden_distraction_row(), 'productive'),
    ('steam-like gaming (app prior 0.10)', gaming_row(0.10), 'fires'),
    ('fullscreen unknown app (prior 0.50)', gaming_row(0.50), 'no_fire'),
]


def predict_inprocess(model_dir):
    from model_inference import ProductivityPredictor
    predictor = ProductivityPredictor(model_dir)
    if not predictor.model_loaded:
        sys.exit(f'FAIL: no model at {model_dir}')

    def predict(features):
        return predictor.predict(features, FEATURE_ORDER)
    return predict


def predict_server(host, port):
    def predict(features):
        with socket.create_connection((host, port), timeout=5) as sock:
            req = {'features': features, 'feature_order': FEATURE_ORDER, 'schema_version': 4}
            sock.sendall((json.dumps(req) + '\n').encode('utf-8'))
            data = b''
            while b'\n' not in data:
                chunk = sock.recv(4096)
                if not chunk:
                    break
                data += chunk
        return json.loads(data.decode('utf-8'))
    return predict


def fires(result):
    return result['prediction'] == 0 and result['confidence'] >= TRIGGER_THRESHOLD


def run_scenarios(predict):
    print(f'{"scenario":<40} {"prediction":<15} {"p(prod)":>8} {"conf":>6}  verdict')
    print('-' * 88)
    failures = 0
    for name, row, expectation in SCENARIOS:
        result = predict(row)
        if not result.get('model_available'):
            print(f'{name:<40} MODEL UNAVAILABLE: {result.get("reason")}')
            failures += 1
            continue
        label = 'PRODUCTIVE' if result['prediction'] == 1 else 'NOT_PRODUCTIVE'
        triggered = fires(result)
        if expectation == 'fires':
            ok = triggered
            want = f'fire >={TRIGGER_THRESHOLD}'
        elif expectation == 'productive':
            ok = result['prediction'] == 1
            want = 'productive'
        else:  # no_fire
            ok = not triggered
            want = 'no fire'
        verdict = 'PASS' if ok else f'FAIL (want {want})'
        failures += 0 if ok else 1
        print(f'{name:<40} {label:<15} {result["probability"]:>8.3f} {result["confidence"]:>6.2f}  {verdict}')
    return failures


def run_sweep(predict, n):
    rng = random.Random(42)
    print(f'\nTrigger-rate sweep ({n} sampled quiet-browsing rows per prior):')
    for name, rate, want_high in [('x.com-like (0.10)', 0.10, True),
                                  ('youtube-like (0.08)', 0.08, True),
                                  ('unknown (0.50)', 0.50, False),
                                  ('docs-like (0.90)', 0.90, False)]:
        hits = sum(1 for _ in range(n) if fires(predict(quiet_browsing_row(rate, rng))))
        pct = 100.0 * hits / n
        marker = '<- should be HIGH' if want_high else '<- should be ~0'
        print(f'  {name:<22} fires {hits}/{n} ({pct:.1f}%)  {marker}')


def main():
    parser = argparse.ArgumentParser(description='V4 seed acceptance check')
    parser.add_argument('--model-dir', default=os.path.join(APP_DIR, 'model_exp'))
    parser.add_argument('--server', action='store_true', help='test a live inference server instead')
    parser.add_argument('--host', default='127.0.0.1')
    parser.add_argument('--port', type=int, default=45003)
    parser.add_argument('--sweep', type=int, default=0, metavar='N', help='also run an N-row trigger-rate sweep')
    args = parser.parse_args()

    predict = predict_server(args.host, args.port) if args.server else predict_inprocess(args.model_dir)

    failures = run_scenarios(predict)
    if args.sweep:
        run_sweep(predict, args.sweep)

    print()
    if failures:
        print(f'FAIL: {failures} scenario(s) failed')
        return 1
    print('PASS: all scenarios')
    return 0


if __name__ == '__main__':
    sys.exit(main())
