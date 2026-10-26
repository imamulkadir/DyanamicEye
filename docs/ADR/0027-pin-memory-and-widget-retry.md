# 0027 — Remembered pin and "Try again" for failed widgets

- Status: Accepted
- Date: 2026-10-26
- Task: P05-02

## Context
- Guide P05-02: persist reorder/pin/preferences; widget configuration errors recover; update UI without full shell
  recreation.
- What already persisted: widget order and on/off (Settings › Widgets, ADR 0003/0018), appearance, behavior, clock and
  shortcut options; all applied in place without recreating the island (settings `Changed` → reposition, re-theme,
  registry apply, presenter re-render). Widget settings errors already recover on load: unknown, duplicate and null
  entries are dropped and missing widgets are added (`SettingsValidator.NormalizeWidgets`).
- Gaps: the pin was session-only, and a widget that **Failed** (after its retries, ADR 0007) stayed failed until the
  user turned it off and on in Settings or restarted the app.

## Decision
1. **Pin is a setting:** `behavior.pinned` (bool, default false; added without a schema bump, ADR 0003 §7).
   `App/PinMemory` restores it at start (Collapsed → TogglePin) and saves it when the user pins or unpins (pin button,
   Ctrl+P, the UI Automation Toggle, Escape). "Pinned" includes the island being hidden for a while and returning
   pinned (fullscreen, pause, tray Hide; `IslandSnapshot.IsPinned`), so those never write settings. Saves run in order
   (quick clicks end with the last state); a failed save is logged by type (`PinNotSaved`) and the next change saves
   again. The Settings window's merge keeps the latest pin (the page does not edit it).
2. **"Try again" on a failed widget's tab:** the note "<widget> isn't available right now." gains a **Try again** button
   (`Retry.<id>`). It calls `WidgetRegistry.RetryAsync` → `ComponentHost.RetryAsync`, which restarts the component only
   when it is enabled, failed, not running, the host is started and not paused, and not in Safe Mode. A restarted
   widget gets a fresh retry budget. Nothing else is restarted.
3. **Last tab:** still not saved (ADR 0025: a disk write per tab switch for little value). Revisit if users ask.

## Alternatives considered
- Saving the whole island state — rejected: Peek/Expanded are transient; only the pin is a user choice.
- Retrying failed widgets automatically on a timer — rejected: a failed widget already used its retry budget; endless
  retries waste battery and log noise. The user decides.

## Consequences
- Settings files gain `"pinned"` under `behavior` when saved; older builds ignore it (unknown properties are ignored).
- Manual rows PN1–PN2 in `docs/TEST_MATRIX.md`.
