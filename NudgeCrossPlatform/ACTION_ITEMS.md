# ACTION_ITEMS

Living doc of what we're actively working on in NudgeCrossPlatform. Update as work lands or scope changes.

---

## Current focus: invisible active-window detection on Linux/KDE

**Problem.** `LinuxPlatformService.GetKDEFocusedAppWithTitle()` in `nudge.cs:420` was stubbed to return the literal `("test-mode", "testing")`. KDE detection has been silently broken. A previous attempt apparently used `org.kde.KWin.queryWindowInfo()` over D-Bus, which puts KWin into an interactive **crosshair window-picker** mode — unacceptable. Detection must be 100% invisible: no crosshair, no popup, no focus-stealing.

**Constraints.**
- Must work on KDE Plasma 6 / Wayland (the dev box).
- No interactive UI of any kind. `org.kde.KWin.queryWindowInfo` is **banned**.
- Should also stay correct on GNOME (already works via `gdbus`/`org.gnome.Shell.Eval`), Sway (already works via `swaymsg`), and generic X11.
- Avoid hard runtime deps that aren't already installed. Use what's there: `xdotool`, `xprop`, `qdbus6`, `Tmds.DBus.Protocol`.

### Iteration 1 — proper KWin script + D-Bus listener (active)

Iteration 1 was originally going to layer xdotool on top of the broken stub, but on the dev box (KDE Plasma 6 Wayland) most apps are native Wayland — xdotool's `_NET_ACTIVE_WINDOW` is empty in that case. So we go straight to the right architecture:

- [x] Document the work in `ACTION_ITEMS.md`.
- [x] Add a `Tmds.DBus.Protocol 0.92.0` package reference to `nudge.csproj`.
- [x] Add `KWinWindowTracker` in `nudge.cs`:
  1. Writes the KWin script package to `~/.local/share/kwin/scripts/nudge-window-tracker/` (idempotent — only rewrites when content differs).
  2. Opens a `DBusConnection` to the session bus, `TryRequestNameAsync("org.nudge.WindowTracker")`, `AddMethodHandler(this)`. Caches `(app, title, updatedAt)` on every `Update(ss)`. KWin's `callDBus` is fire-and-forget so we don't write a reply.
  3. Loads + starts the script via `qdbus6 org.kde.KWin /Scripting`. We unload-then-load so script content updates pick up immediately.
- [x] Wire `LinuxPlatformService.GetKDEFocusedAppWithTitle()` to return the tracker's cached value, falling back to xprop on XWayland (still invisible) only if the tracker hasn't reported yet.
- [x] Replace `GetX11FocusedAppWithTitle()` with EWMH read: `xprop -root _NET_ACTIVE_WINDOW` → `xprop -id <id> WM_CLASS _NET_WM_NAME WM_NAME`. The `/proc` heuristic is now last-resort only.
- [x] Build `nudge.csproj` → 0 errors. Smoke test on the dev box (KDE Plasma 6 Wayland) shows native Wayland apps being tracked correctly: `Switched: unknown → org.kde.konsole`. No crosshair.

Known minor: at the very first `GetForegroundApp()` call during startup, the tracker can still be a few ms ahead of the KWin script's first publish, so the startup self-test prints a one-time `Could not detect foreground window` warning. From the next cycle onward it's correct. Not worth a sync wait — it's purely cosmetic.

Files touched / added this iteration:
- `nudge.cs` — added `using Tmds.DBus.Protocol;`, new `KWinWindowTracker` class, replaced `GetKDEFocusedAppWithTitle` and `GetX11FocusedAppWithTitle` (added `ReadActiveX11Window` + `ExtractQuotedTokens` helpers).
- `nudge.csproj` — added `Tmds.DBus.Protocol` package reference.
- `~/.local/share/kwin/scripts/nudge-window-tracker/` — auto-installed at runtime, not in git.
- `ACTION_ITEMS.md` — this file.

### Iteration 2 — optional polish

- [ ] Subscribe to per-window `captionChanged` signals from inside the KWin script so title-only changes (e.g. browser tab switch in a Wayland-native browser) re-publish without needing a focus event.
- [ ] Investigate `ext-foreign-toplevel-list-v1` (Wayland standardized, supported by KWin and Mutter). It exposes title + `app_id` + stable identifier per toplevel, but **does not expose active/focus state**. Could combine with a focus signal to make detection portable across compositors.
- [ ] Consider a Windows-side audit (the Win32 `GetForegroundWindow` path is already correct and invisible — leave it unless we find a specific bug).

### Banned / dead-ends (do not retry)

- `org.kde.KWin.queryWindowInfo` D-Bus call → triggers a crosshair window-picker. Do not put it in source. Do not invoke it from a shell to "test."
- `kde-plasma-window-management` Wayland protocol → spec explicitly states "Regular clients must not use this protocol" and "Only one client can bind this interface at a time" (the task manager owns it). Not available to us.

---

## Notes

- Feedback memory: `feedback_no_kwin_query_window_info.md` — the queryWindowInfo ban is recorded so future sessions don't re-attempt it.
- Dev box: KDE Plasma 6 Wayland on CachyOS. `xdotool`, `xprop`, `qdbus6`, `gdbus`, `dbus-send` all installed; `kdotool`, `swaymsg`, `wmctrl` are not.
