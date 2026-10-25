# 0010 — Fullscreen detection and auto-hide

- Status: Accepted
- Date: 2026-10-25
- Task: P02-04

## Context
- D06: auto-hide for fullscreen apps and games, with a manual override in settings.
- Guide P02-04: game-safe hide and restore, configurable exceptions, and never assuming an overlay over exclusive fullscreen works.
- D10: event-driven, no unbounded polling.
- ADR 0004: no window titles or personal data in logs.

## Decision
1. **Detector (`Platform.Windows/Display/Win32FullscreenDetector`, guide §3 `IFullscreenDetector`).**
   - Out-of-context `SetWinEventHook`s run on the UI thread: `EVENT_SYSTEM_FOREGROUND`, `EVENT_SYSTEM_MINIMIZEEND`, and `EVENT_OBJECT_LOCATIONCHANGE` filtered to the current foreground window (it fires for every window, so everything else is ignored). There is no timer or polling.
   - A window counts as fullscreen when **all** of these hold:
     - it covers its monitor's **full bounds**, including the taskbar area
     - it is not "maximized with a caption", because a normal maximized app can cover the bounds when the taskbar auto-hides
     - it is visible, not minimized and not DWM-cloaked
     - it is not the shell or desktop (`Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, `GetShellWindow`)
     - it does not belong to the app's own process
   - `SHQueryUserNotificationState` adds `QUNS_RUNNING_D3D_FULL_SCREEN` and `QUNS_PRESENTATION_MODE`. These carry no monitor, so the island hides on any monitor; `QUNS_BUSY` alone is ignored.
   - The snapshot holds the monitor id (ADR 0008 scheme) and the executable file name, **used only for exceptions and never logged**.
2. **Policy (`Core/Display/FullscreenPolicy`, pure).** The island hides only if all of these hold:
   - `Behavior.AutoHideInFullscreen` is on
   - something is fullscreen
   - the app is not in `Behavior.FullscreenExceptions`
   - the fullscreen window is on **the island's monitor**, or its monitor is unknown

   A fullscreen video on the other monitor leaves the island visible.
3. **Exceptions setting:** `Behavior.FullscreenExceptions` lists executable **file names only** (no paths, `*.exe`), trimmed, lower-cased, de-duplicated, at most 50. It is a new property with a default, so there is no schema bump. `BehaviorSettings` defines value equality over the list.
4. **Coordinator (App).** Detector changes go through a 300 ms debounce (so alt-tabbing doesn't flicker), then the policy, then `FullscreenStarted`/`FullscreenEnded` on the island state machine (ADR 0009). The machine remembers whether the island was pinned or collapsed and restores that.
   - The log records only "island hidden True/False".
   - The detection hooks are removed when the detector is disposed (with the host).
5. **Honest limitation:** exclusive-fullscreen (D3D) games may draw above **any** topmost window, and secure desktops (UAC, lock screen) always do. The island never tries to force itself on top. It hides, and reappears when the game leaves fullscreen.

## Amendment — cloaked windows (P02-05, 2026-10-25)
- **Problem:** CI run 40205375218 failed "island must hide", and the failure later reproduced locally in Release. The detection hooks were installed (logged), yet fullscreen was never reported.
- **Cause:** a newly shown window can still be **DWM-cloaked** (opening animation) when its foreground event arrives. It was skipped as a candidate, and no later event re-evaluated it.
- **Fix:**
  - Hook `EVENT_OBJECT_UNCLOAKED`, filtered to the foreground window.
  - Run one bounded follow-up re-evaluation 0.5 s after each foreground change, as a one-shot timer, not polling.
  - `Refresh` is now thread-safe.
  - The startup log records the hook count (4 expected).
- **Result:** Release smoke 3/3 and full suites green in both parallel and sequential mode.

## Testing
- Unit: policy (monitor match, exceptions, unknown monitor, setting off) and exception validation.
- Integration: real helper windows on the test host, both covering the monitor and smaller, plus a maximized captioned window and own-process windows.
- Smoke (end to end): a fullscreen helper becomes foreground, the real island hides and then returns.
  - Windows decides whether a background test process may take the foreground. When it refuses, the test is **reported as skipped**, never as passed.
  - On the dev host it ran in Release and was skipped in Debug.

## Consequences
- Helper windows in tests must not be topmost. `IslandWindowInterop.SetBounds(..., topmost: false)` exists for that, because parallel test projects could otherwise cover or hide the real island.
- P03 settings UI edits the exceptions list. P05 power and performance work may pause the hooks while the app is paused.
