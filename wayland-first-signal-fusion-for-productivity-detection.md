# Wayland-First Signal Fusion for Productivity Detection

## Summary

Nudge should stop treating “current app” as the only semantic signal and instead build a small fused activity context from portable Wayland metadata plus short-term behavior features. The portable baseline should assume access to `app_id`, toplevel `title`, seat idle/resume, and window lifecycle events when available; anything beyond that is optional enrichment.

The first implementation should optimize for:
- Local-only storage
- Semantic detail allowed: raw normalized titles and browser domains
- Best available runtime behavior: portable Wayland baseline, then non-standard protocol enrichers, then compositor-specific enrichers

This work should extend the current pipeline in [nudge.cs](/home/sammy/Dev/Nudge/NudgeCrossPlatform/nudge.cs), [NudgeJsonContext.cs](/home/sammy/Dev/Nudge/NudgeCrossPlatform/NudgeJsonContext.cs), [model_inference.py](/home/sammy/Dev/Nudge/NudgeCrossPlatform/model_inference.py), and [train_model.py](/home/sammy/Dev/Nudge/NudgeCrossPlatform/train_model.py).

## Why This Changes the Model

The current model only sees:
- `foreground_app`
- `idle_time`
- `time_last_request`
- `hour_of_day`
- `day_of_week`

That is too weak for the common real-world failure mode: the user is still “using Firefox” or “using Chrome”, but the semantic context has shifted from docs, code review, or Linear to YouTube, Reddit, or random tab drift. The model needs session-level context, not just process identity.

## User Scenarios to Design For

- Deep work: one editor, terminal, docs, or issue tracker stays dominant for 10-60 minutes; input stays active; app/site set is narrow; return pattern is stable.
- Productive communication: Slack, email, calendar, and docs switch frequently, but the set of apps/sites is still work-bounded and the user returns to an anchor app.
- Passive distraction: browser remains foreground, but title/domain shifts to entertainment or social content; dwell grows; input becomes sparse; fullscreen may appear.
- Fragmented distraction: many short switches across chat, browser, notifications, and utilities; no sustained anchor; distinct apps/sites per 5-10 minutes rises.
- AFK: idle increases sharply; this should not be learned as “unproductive work”, but as absence/unknown context.
- Meetings/calls: long dwell with lower typing can still be productive; app/site identity matters more than input volume alone.

## Portable Wayland Signal Inventory

- `wl_seat` is not a global foreground-app API. It only defines input devices and focus relative to client surfaces, so it cannot by itself tell Nudge which foreign app is focused.
- `xdg-shell` defines `set_app_id` and `set_title`, which are the portable semantic labels apps provide for their toplevels.
- `ext_foreign_toplevel_list_v1` is the portable window-enumeration baseline. It exposes mapped toplevel handles plus `app_id`, `title`, and a stable `identifier`, but it does not expose active/focused state.
- `ext_idle_notify_v1` gives compositor-driven idle and resume notifications tied to a seat, which is a better Wayland-native idle source than shelling out.
- `ext_workspace_v1` can expose active workspace state on supporting compositors, but support is uneven and it does not solve focused-window identity by itself.

## Collection Strategy

- Build a new `SignalCollector` layer that produces a unified `ActivityContext` once per second.
- Prefer Wayland-native idle via `ext_idle_notify_v1`. Keep existing idle fallback paths for environments without that protocol.
- Prefer a Wayland toplevel catalog via `ext_foreign_toplevel_list_v1`. Maintain an in-memory map of `identifier -> {app_id, title, first_seen_at, last_changed_at}`.
- Treat portable Wayland as catalog-plus-idle, not catalog-plus-focus. Focus must still come from a best-available focus source.
- Keep the current compositor-specific focus detectors as the active-window source until a portable activated-state protocol is available on the runtime compositor.

## Fusion Model V1

Create two layers:

- `ActivityContext`: raw observation for the current tick.
- `FeatureVectorV2`: derived numeric features sent to ML.

`ActivityContext` fields:
- `focused_app_id`
- `focused_title`
- `focused_domain`
- `focused_window_id`
- `idle_ms`
- `is_idle_now`
- `focused_since_ms`
- `title_unchanged_for_ms`
- `mapped_toplevel_count`
- `active_workspace_id` when available
- `focus_source` enum
- `signal_quality` enum

`FeatureVectorV2` fields:
- `hour_of_day`
- `day_of_week`
- `focused_app_hash`
- `focused_domain_hash`
- `idle_ms`
- `focused_since_ms`
- `title_stability_ms`
- `switch_count_60s`
- `switch_count_300s`
- `distinct_apps_300s`
- `distinct_domains_300s`
- `returned_to_anchor_app_300s`
- `current_app_share_300s`
- `current_domain_share_300s`
- `browser_window_flag`
- `communication_app_flag`
- `entertainment_domain_flag`
- `work_domain_flag`
- `afk_flag`
- `fullscreen_flag` when available
- `workspace_switch_count_300s` when available

## Derived Feature Rules

- `focused_domain` comes from the existing browser title parsing logic. For non-browsers, leave empty.
- `returned_to_anchor_app_300s` is `1` if the current app matches the most common app in the last 5 minutes after at least one intervening switch.
- `current_app_share_300s` is the fraction of 1-second samples in the last 5 minutes spent in the current app.
- `current_domain_share_300s` is the same metric at domain granularity.
- `communication_app_flag` is `1` for chat, mail, calendar, and conferencing apps/sites.
- `entertainment_domain_flag` is `1` for known entertainment/social/video domains.
- `work_domain_flag` is `1` for known work tools such as docs, GitHub, Jira, Linear, Figma, Slack, mail, localhost, and internal domains if configured later.
- `afk_flag` is `1` once idle exceeds 60 seconds. Rows with `afk_flag=1` should be excluded from training by default.
- `signal_quality` should be downgraded if focus came from a heuristic path, if title is empty for browsers, or if focus and catalog disagree.

## Runtime Source Priority

- Priority 1: protocol enrichers that expose activated state.
- Priority 2: compositor-specific focus adapters.
- Priority 3: current X11/XWayland fallbacks.
- Priority 4: heuristic process scan only as last resort, and never as a trusted training signal.

When focus is low-confidence:
- Keep collecting raw activity log rows.
- Do not emit labeled ML training rows unless `signal_quality >= usable`.

## Protocol and Compositor Options

Portable or semi-portable enrichers:
- `ext_foreign_toplevel_list_v1`: unlocks portable `app_id`, `title`, toplevel identity, and window-change timing.
- `ext_idle_notify_v1`: unlocks compositor-native idle/resume.
- `ext_workspace_v1`: unlocks active workspace changes where available.
- `wlr-foreign-toplevel-management-unstable-v1`: unlocks explicit `activated`, `fullscreen`, `minimized`, and `maximized` state on wlroots-family compositors and other adopters.
- `xx-linux-foreign-toplevel-pidfd-v1`: unlocks pidfd-to-process mapping for exact process identity on supporting compositors.

Compositor-specific adapters:
- Sway IPC: unlocks exact focused node, `app_id`, title, workspace, output, urgent state, and tree structure. This is the richest Linux path today.
- KWin script/D-Bus: unlocks exact active window plus title changes without polling. It can also expose fullscreen and workspace data if needed.
- GNOME Shell D-Bus/Eval: unlocks exact focused window class/title and shell context, but is shell-specific and less future-proof.

## Interface Changes

Add a new raw event record and a versioned ML request:
- `ActivityContextRecord`
- `MLPredictionRequestV2`

`MLPredictionRequestV2` should be JSON object based, not a positional fixed-width vector. Python should read named keys and assemble the feature vector from a declared ordered feature list. This removes the current tight coupling to a 5-field record.

CSV changes:
- Keep `HARVEST.CSV` backward-compatible by appending new columns.
- Add a companion `FEATURES.CSV` or extend `HARVEST.CSV` with all engineered features plus `signal_quality`.
- Add `focused_title`, `focused_domain`, `focus_source`, and `signal_quality` to `ACTIVITY_LOG.CSV`.

Training changes:
- `train_model.py` should detect schema version and use an explicit ordered feature list from file headers.
- Rows with `afk_flag=1` or `signal_quality=poor` should be dropped by default.
- Raw text fields stay in CSV for inspection but are not fed directly to the model in V1.

## Acceptance Criteria

- Browser work vs browser distraction becomes separable in the dataset without relying on compositor-specific app names.
- Idle is sourced from Wayland protocols when available.
- Focus-source provenance is logged on every row.
- Heuristic focus rows do not pollute training by default.
- Model input becomes extensible without breaking older datasets.

## Test Cases and Scenarios

- Browser title/domain parsing: work tabs, entertainment tabs, generic titles, empty titles.
- Rolling-window features: switch counts, distinct domains, anchor-return detection, dwell-time accumulation.
- AFK filtering: rows over idle threshold are excluded from training.
- Focus-source downgrade: heuristic or unknown focus marks `signal_quality=poor`.
- Portable protocol absence: collector falls back cleanly without crashing.
- Sway path: focused node changes update `ActivityContext` correctly.
- KDE path: title-only tab changes update context without requiring app switch.
- Mixed session: Slack plus editor plus docs remains distinguishable from Reddit plus YouTube plus random browsing.

## Assumptions and Defaults

- Store normalized raw titles and browser domains locally.
- Optimize for “best available” runtime behavior, not strict portable-only purity.
- Do not use screenshots, OCR, clipboard scraping, or keystroke logging in V1.
- Do not treat raw idle alone as unproductive.
- Do not trust process-scan fallback for training labels.
- Keep model numeric and small; no NLP model on titles in V1.

## Sources

- [Wayland core protocol](https://wayland.freedesktop.org/docs/html/apa.html)
- [xdg-shell](https://wayland.app/protocols/xdg-shell)
- [ext-foreign-toplevel-list-v1](https://wayland.app/protocols/ext-foreign-toplevel-list-v1)
- [ext-idle-notify-v1](https://wayland.app/protocols/ext-idle-notify-v1)
- [ext-workspace-v1](https://wayland.app/protocols/ext-workspace-v1)
- [wlr-foreign-toplevel-management-unstable-v1](https://wayland.app/protocols/wlr-foreign-toplevel-management-unstable-v1)
- [xx-linux-foreign-toplevel-pidfd-v1](https://wayland.app/protocols/xx-linux-foreign-toplevel-pidfd-v1)
- [COSMIC toplevel info](https://wayland.app/protocols/cosmic-toplevel-info-unstable-v1)
