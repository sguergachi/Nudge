# Proposal: Reliable Windows Meeting Detection

> **Status:** ✅ Implemented — event-driven `ConsentStore` watcher
> (`WindowsPresenceWatcher` in `nudge.cs`) plus pure `ConsentStorePresence`
> decision logic in `NudgeCore.TestableLogic.cs` with cross-platform unit tests.
> This document remains the design rationale.
> **Scope:** Replace the current polled signal-fusion presence detector on Windows
> with an event-driven detector built on the same authoritative source Windows
> itself uses to draw the camera/mic privacy indicator.
> **Files touched by the eventual implementation:**
> [nudge.cs](NudgeCrossPlatform/nudge.cs),
> [NudgeCore.TestableLogic.cs](NudgeCrossPlatform/NudgeCore.TestableLogic.cs),
> [NudgeMeetingGateTests.cs](NudgeCrossPlatform/NudgeCrossPlatform.Tests/NudgeMeetingGateTests.cs).

## TL;DR

The current Windows detector ([`GetPresenceState`](NudgeCrossPlatform/nudge.cs) at
`nudge.cs:1101`) **polls every 5 seconds** and fuses five weak signals (Core Audio
capture-session enumeration, registry mic, registry camera, window title, process
scan) into a weighted score with a `0.40` threshold. This is laggy (up to 5 s to
notice a meeting), CPU-heavy (a full `Process.GetProcesses()` walk plus COM
enumeration every tick), and produces both false positives (any mic use scores)
and false negatives (capture-session enumeration is unreliable across Windows
builds).

**Proposed replacement:** read the **CapabilityAccessManager `ConsentStore`**
keys — the exact source the OS privacy indicator is built on — across **both
`HKCU` and `HKLM`**, for both `microphone` and `webcam`, and **subscribe to
change notifications with `RegNotifyChangeKeyValue`** instead of polling. This
gives near-instant, near-zero-idle-CPU, authoritative mic/camera state. Process
and title matching are demoted to an optional *classifier* (is this mic use a
*meeting* vs. dictation?), not the primary detector. Screen-share detection stays
best-effort and is documented as such.

---

## 1. What we have today

### 1.1 The fusion scorer (`nudge.cs:1101`–`1155`)

```
Weight  Signal          How it's read
0.40    audioActive     IAudioSessionManager2 capture-session enumeration (COM)
0.30    camApps         ConsentStore\webcam   (HKCU only)
0.20    micApps         ConsentStore\microphone (HKCU only)
0.10    titleMatch      foreground window title keyword match
0.10    appRunning      Process.GetProcesses() name match
threshold = 0.40, + 3 s flicker cooldown
```

The result is collapsed into `PresenceState(inMeeting, false, false, WindowsRegistry)` —
note **camera and screen-share are always reported `false`** on Windows even
though the camera registry key is read; everything is mashed into the single
`InMeeting` bit.

### 1.2 How it's consumed

`nudge.cs:1879` polls `GetPresenceState()` every 5 s into `_cachedPresenceState`,
prints `MEETING:mic|cam|screen`, and `SnapshotGate.Evaluate`
([`NudgeCore.TestableLogic.cs:1796`](NudgeCrossPlatform/NudgeCore.TestableLogic.cs))
suppresses nudges when `presence.InMeeting || presence.IsScreenSharing` and
`Source != None`.

### 1.3 Why it's unreliable on Windows

| Problem | Cause | Effect |
|---|---|---|
| **5 s detection lag** | 5 s poll cadence | Meeting starts → user can get nudged for up to 5 s; meeting ends → suppressed for up to 5 s extra |
| **Capture-session enumeration is flaky** | `IAudioSessionManager2::GetSessionEnumerator` on the *capture* endpoint is documented as inconsistent across Windows 10/11 builds; many apps' mic sessions never enumerate | The 0.40 "strongest" signal silently returns `false` → relies on weaker signals |
| **HKCU-only registry read** | `TestCapability` only opens `Registry.CurrentUser` | Misses `HKLM\...\ConsentStore` where **services and some packaged apps** record usage → false negatives |
| **Packaged-app blind spots** | new Teams (`MSTeams_8wekyb3d8bbwe`) is a packaged app; its key is a direct PFM subkey, not under `NonPackaged` | Code handles both, but only in HKCU |
| **False positives from "any mic"** | mic-in-use scores 0.20; title or app match adds 0.10 each → 0.40 reached by dictation + Teams running in tray | Suppresses nudges when not actually in a meeting |
| **CPU cost** | full process enumeration + COM activation **every 5 s, forever** | Wasted cycles on a tool meant to run silently in the background |
| **Single bit out** | camera/screen forced to `false` | Analytics `MEETING:` line and the gate can never distinguish camera-only or screen-share |

---

## 2. The key insight

Windows already solves "is the camera/mic in use right now" — that's how it draws
the privacy indicator in the system tray and the Settings → Privacy "recent
activity" list. The source of truth is:

```
HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{microphone,webcam}
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{microphone,webcam}
```

Under each capability key:
- **Packaged apps** → direct subkey named by Package Family Name (e.g.
  `MSTeams_8wekyb3d8bbwe`, `Microsoft.ScreenSketch_...`).
- **Desktop apps** → under `NonPackaged\`, with the exe path mangled (`#` for `\`),
  e.g. `C:#Program Files#Zoom#bin#Zoom.exe`.

Each leaf has two REG_QWORD FILETIME values:
- `LastUsedTimeStart` — set when the device starts being used.
- `LastUsedTimeStop` — **set to `0` while in use**, stamped with the stop time
  when use ends.

**`LastUsedTimeStop == 0 && LastUsedTimeStart != 0` ⇒ in use right now.** This is
exactly the rule the OS itself uses, available since Windows 10 1903. It is far
more reliable than enumerating audio capture sessions, and it covers every app
that goes through the standard capability broker (which, on modern Windows, is
all of them — Teams, Zoom, Slack, Discord, Chrome/Edge WebRTC, etc.).

Crucially, **these keys can be watched** with
[`RegNotifyChangeKeyValue`](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regnotifychangekeyvalue):
register once with `bWatchSubtree = TRUE` and `REG_NOTIFY_CHANGE_LAST_SET`, and
Windows signals an event handle the instant any subkey value changes — i.e. the
moment a mic/camera turns on or off. No polling.

---

## 3. Proposed design

### 3.1 Detector: event-driven `ConsentStore` watcher

A new Windows-only `WindowsPresenceWatcher` (in `nudge.cs`, behind `#if WINDOWS`):

1. **On start**, open the four keys (`HKCU`/`HKLM` × `microphone`/`webcam`).
   Compute an initial snapshot by scanning for any leaf with
   `LastUsedTimeStop == 0 && LastUsedTimeStart != 0`.
2. **Register notifications** on each open key with
   `RegNotifyChangeKeyValue(hKey, watchSubtree:true, REG_NOTIFY_CHANGE_LAST_SET,
   hEvent, async:true)` and block a single background thread on
   `WaitHandle.WaitAny(events + stopEvent)`.
3. **On any signal**, re-scan that capability's keys (cheap — a few subkeys),
   update an atomic `volatile PresenceSnapshot`, re-arm the notification, and
   emit a `MEETING:` line **only on change** (event-driven, not every 5 s).
4. The main loop reads the cached snapshot (lock-free) instead of calling into
   the registry/COM. `GetPresenceState()` becomes a trivial accessor.

This pushes detection latency from "up to 5 s" to "tens of milliseconds" and idle
CPU from "scan every 5 s forever" to "blocked on a wait handle."

### 3.2 Active-app attribution & staleness (keep the good parts)

The existing **staleness guard** in `IsCurrentlyActive` (`nudge.cs:1402`) — verify
the owning exe is actually running before trusting an "active" entry — is genuinely
valuable and should be **kept**, because Windows occasionally fails to stamp
`LastUsedTimeStop` when an app is force-killed. For packaged apps (PFM subkeys),
add a Package-Family lookup or simply trust them (they can't be killed without the
broker noticing) to avoid false negatives.

The mangled `NonPackaged` exe path also gives us **which app** is using the device
for free — feed that into the meeting-vs-not classifier below and into analytics.

### 3.3 Meeting classifier: separate "device in use" from "in a meeting"

Reading the ConsentStore tells us *a device is in use*; it does not tell us *the
user is in a meeting* (dictation, voice typing, a podcast recorded in Audacity, or
a webcam-based barcode scanner all light up the mic/camera). Today the fusion
score conflates these. Proposed split:

- **`IsMicActive` / `IsCameraActive`** ← authoritative, straight from ConsentStore.
- **`InMeeting`** ← `(IsMicActive || IsCameraActive)` **AND** the owning app (from
  the registry leaf name) or the foreground window is a known communication app
  (`MeetingTitleDetector.ProcessNames` / `TitleKeywords`, already in
  [`NudgeCore.TestableLogic.cs:1908`](NudgeCrossPlatform/NudgeCore.TestableLogic.cs)).
  Camera-on is treated as meeting regardless of app (camera-on for non-meeting
  reasons is rare and the suppression cost is low).

This removes the brittle weighted-threshold math entirely and replaces it with an
explainable rule, while reusing the existing keyword/process lists. Because the
device signal is now reliable, we no longer need title/process matches to *reach a
threshold* — they only *classify* an already-confirmed signal.

### 3.4 Carry camera & screen-share through honestly

Change the Windows return from `PresenceState(inMeeting, false, false, …)` to
`PresenceState(micActive, camActive, screenSharing, WindowsRegistry)` so the
`MEETING:mic|cam|screen` line and the gate reflect reality. The
`SnapshotGate.Evaluate` logic already handles all three bits — no gate change
needed.

### 3.5 Screen sharing — explicitly best-effort

There is **no authoritative public registry/API signal** for "a screen is being
captured right now" (the Windows.Graphics.Capture yellow border is drawn by the
shell with no public query API). Options, in order of preference:

1. **Best-effort heuristic (recommended for v1):** keep `IsScreenSharing` derived
   from a known meeting app being foreground while mic/cam is active. Document it
   as best-effort. This matches current behavior (which never set it anyway) but
   wires the bit through.
2. **Future:** investigate `IGraphicsCaptureSession` enumeration via WinRT or DWM
   cloaking heuristics. High effort, uncertain payoff — out of scope for v1.

### 3.6 New source enum value (optional but clearer)

Rename/add `PresenceSource.WindowsConsentStore` (keep `WindowsRegistry` as an alias
to avoid churn, or just reuse it). Purely cosmetic; the gate only cares about
`Source != None`.

---

## 4. Reference sketch (illustrative, not final)

```csharp
#if WINDOWS
// P/Invoke
[DllImport("advapi32.dll")]
static extern int RegOpenKeyEx(IntPtr hKey, string subKey, int opts, int sam, out IntPtr phkResult);
[DllImport("advapi32.dll")]
static extern int RegNotifyChangeKeyValue(IntPtr hKey, bool watchSubtree,
    int notifyFilter, SafeWaitHandle hEvent, bool asynchronous);

const int REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;
const int KEY_NOTIFY = 0x0010, KEY_READ = 0x20019;

sealed class WindowsPresenceWatcher : IDisposable
{
    volatile PresenceState _state = PresenceState.Unavailable;
    public PresenceState Current => _state;

    // 4 watched keys: {HKCU,HKLM} x {microphone, webcam}
    // background thread: WaitAny(events); on signal -> Rescan(); re-arm.

    static bool ScanCapability(RegistryKey root)  // returns "any leaf in use"
    {
        // walk direct subkeys (packaged) + NonPackaged\* (desktop)
        // leaf in use  <=>  LastUsedTimeStart != 0 && LastUsedTimeStop == 0
        // keep existing staleness guard for NonPackaged exes
    }
}
#endif
```

The leaf-scanning rule and the packaged/NonPackaged traversal are **already
implemented and unit-testable** in the current `CountActiveApps` /
`IsCurrentlyActive` helpers — the change is (a) add HKLM, (b) wrap in a watcher,
(c) drop the COM audio path and the weighted score.

---

## 5. Testability

The hard-to-test part (registry + Win32 notifications) is isolated in
`WindowsPresenceWatcher`. The *decision logic* is pulled into a pure function in
`NudgeCore.TestableLogic.cs`, mirroring the existing `PipeWireParser` /
`PulseAudioParser` pattern, so it runs cross-platform in CI:

```csharp
internal static class ConsentStorePresence
{
    // Pure: given parsed leaf records, produce a PresenceState + meeting classification.
    public static PresenceState Evaluate(
        IReadOnlyList<ConsentLeaf> micLeaves,
        IReadOnlyList<ConsentLeaf> camLeaves,
        string foregroundProcess, string foregroundTitle);
}
internal readonly record struct ConsentLeaf(string AppId, long StartFileTime, long StopFileTime, bool ProcessRunning);
```

New tests to add to `NudgeMeetingGateTests.cs`:

- `Stop==0,Start!=0` ⇒ active; `Stop!=0` ⇒ inactive; `Start==0` ⇒ inactive.
- Stale NonPackaged entry (process not running) ⇒ inactive.
- Packaged PFM leaf (e.g. `MSTeams_*`) active ⇒ mic active.
- HKLM-only active leaf ⇒ detected (regression guard for the HKCU-only bug).
- Mic active + meeting app foreground ⇒ `InMeeting`; mic active + Notepad
  dictation foreground ⇒ **not** `InMeeting` (mic active but no meeting).
- Camera active ⇒ `InMeeting` regardless of app.
- Existing `SnapshotGate.Evaluate` tests remain unchanged (gate API is stable).

## 6. Migration / rollout

1. Land `ConsentStorePresence.Evaluate` + tests (pure, no behavior change).
2. Add `WindowsPresenceWatcher`; have `GetPresenceState()` return its cached
   snapshot. Keep the 5 s `MEETING:` broadcast as a *heartbeat* but source it from
   the watcher's cache (the watcher also emits on change).
3. Delete the Core Audio COM interfaces and the weighted-score block once the
   watcher is validated (they're ~150 lines of fragile P/Invoke at
   `nudge.cs:1199`–`1356`).
4. Keep `--no-meeting-suppression` and the fail-open contract (`Source == None`
   when detection genuinely unavailable, e.g. pre-1903) intact.

## 7. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Pre-1903 Windows lacks ConsentStore | Detect missing keys → `Source = None` (fail open, current behavior) |
| HKLM read needs no admin (read-only) | `RegOpenKeyEx` with `KEY_READ` works for standard users |
| `RegNotifyChangeKeyValue` is one-shot | Re-arm after every signal (standard pattern) |
| App force-killed without `Stop` stamp | Keep the existing process-running staleness guard |
| Some WebRTC apps record under the browser PFM, not the meeting app | Camera-on still classifies as meeting; mic-only-in-browser is the one residual ambiguity — acceptable, and better than today |

## 8. Expected outcome

- **Latency:** ~5 s → sub-second.
- **Idle CPU:** periodic process+COM scan → blocked wait handle.
- **Correctness:** authoritative OS signal across HKCU+HKLM, packaged + desktop;
  camera and screen bits reported honestly; meeting vs. mere-mic-use disambiguated.
- **Maintainability:** ~150 lines of flaky COM deleted; decision logic unit-tested
  cross-platform alongside the existing PipeWire/PulseAudio parsers.

---

### Sources

- [CapabilityAccessManager ConsentStore — Velociraptor artifact docs](https://docs.velociraptor.app/exchange/artifacts/pages/windows.registry.capabilityaccessmanager/)
- [Tracking Microphone and Camera Usage in Windows (CapabilityAccessManager)](https://www.cyberengage.org/post/registry-system-configiuration-tracking-microphone-and-camera-usage-in-windows-program-execution)
- [MS Teams "On Air" sign — using the registry to monitor webcam/mic use](https://davidarno.org/using-the-registry-to-monitor-webcam-and-microphone-use/)
- [Can you track processes accessing the camera and microphone? (svch0st)](https://svch0st.medium.com/can-you-track-processes-accessing-the-camera-and-microphone-7e6885b37072)
- [RegNotifyChangeKeyValue — Win32 API](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regnotifychangekeyvalue)
- [Screen capture (Windows.Graphics.Capture) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture)
