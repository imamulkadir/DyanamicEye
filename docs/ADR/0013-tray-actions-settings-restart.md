# 0013 — Tray actions, settings service, restart contract and shutdown mode

- Status: Accepted
- Date: 2026-10-26
- Task: P03-02

## Context
- Guide P03-02: the tray menu offers show/hide, pin, pause/resume, display selection, settings, update check, logs,
  restart in or out of Safe Mode, and Exit. Every action must be keyboard accessible and must not crash the app.
- Several actions change settings at runtime, so the app needs one owner of the live settings instead of the read-once
  `AppSettingsSnapshot` from P01-01 (ADR 0003).
- Restart must work both under the launcher (ADR 0006) and when the user started `DynamicIsland.App.exe` directly, and
  must not race the single-instance lock (ADR 0005).
- Settings (P03-03) will open more windows. Closing one of them must never shut the app down.

## Decision
1. **Tray menu model.** `Core/Shell/TrayMenu.cs` (`TrayMenuModel.Build`) is a pure, unit-tested model; `TrayController`
   renders it as a WPF `ContextMenu` and executes the commands. Items, in order:
   - Show/Hide island; Pin (enabled only while the island is visible); Pause/Resume.
   - **Display** submenu: "Display N (primary)" ordered left to right, the current monitor checked. Monitor ids are
     command arguments only and are never shown or logged. **Reset to primary display** also resets the offsets; it is the
     recovery path for an island the user has lost off-screen.
   - Settings… and Check for updates: **disabled with a tooltip** until P03-03 and P06 deliver them.
   - Open logs folder; Restart in Safe Mode / Leave Safe Mode; Restart; Exit.
   - Every item has an AutomationId `Tray.*`, which the smoke tests use through UI Automation.
2. **Settings service.** `Core/Settings/SettingsService.cs` (`ISettingsService`) replaces `AppSettingsSnapshot`, which is
   deleted. It exposes `Current`, `IsReadOnly` and `Changed`. `UpdateAsync` is serialized and runs change → validate →
   save → publish.
   - A read-only store (portable folder on read-only media) applies the change for the session only.
   - A failed save keeps the old settings and logs `SettingsNotSaved`; the tray never crashes on it.
   - `IslandWindow` re-places itself and re-applies its state on `Changed`.
3. **Restart contract** (`Core/Lifecycle/ExitReason.cs` → `LauncherContract`):
   - New `ExitReason.RestartInSafeMode` = **exit code 4**, next to `Restart` = 3. The launcher relaunches in Safe Mode on
     4 and normally on 3. Neither counts as a crash; both share the existing cap of 5 restart requests per launcher
     lifetime (ADR 0006).
   - The launcher sets `DYNAMICISLAND_SUPERVISED=1` for its child.
   - The launcher accepts only the allow-listed `--safe-mode` and `--after-pid <positive int>`
     (`Core/Lifecycle/LauncherArguments.cs`); anything else is counted and ignored. With `--after-pid` it waits up to 15 s
     for that process to exit, so the single-instance lock is free.
   - `App/RestartService.cs`: when supervised, the app just exits with 3/4. When started directly, it starts the launcher
     next to it with `--after-pid <own pid> [--safe-mode]` and exits. Without a launcher next to the app the restart is
     refused and logged (`RestartUnavailable`).
4. **Shutdown mode.** `App.xaml` sets `ShutdownMode="OnExplicitShutdown"`. The island window's `Closing` handler turns
   Alt+F4 or an external close request into `RequestShutdown(UserExit)`, so the lifecycle stays the only path to exit and
   future windows can close freely.
5. **Pause.** Tray Pause moves the state machine to `Paused` and calls `ComponentHost.SetPausedAsync(true)`, which stops
   optional components (status Stopped, reason `paused`). Resume restarts them; `ComponentBoundary` is now restartable
   after a stop. A component that does not stop in time is logged (`ComponentPauseIncomplete`).
6. **New log events** (no free text, ADR 0004): `RestartRequested`, `RestartUnavailable`, `SettingsNotSaved`,
   `OpenLogsFailed`, `ComponentPauseIncomplete`.

## Alternatives considered
- Restart by starting a second copy of the app directly — rejected: it races the single-instance lock and loses crash
  supervision.
- A free-form launcher command line forwarded to the app — rejected: the command line is untrusted input (guide §4.6).
- Keeping `ShutdownMode="OnMainWindowClose"` — rejected: a settings window or a recreated island window could end the app.
- Per-feature settings writers — rejected: concurrent saves could lose updates; one serialized service is simpler.

## Consequences
- P03-03 binds the settings window to `ISettingsService` and enables the Settings… item. P06 enables Check for updates.
- The launcher must be shipped next to the app in both the portable and installed layouts, or Restart is unavailable.
- Manual verification rows TA1–TA10 in `docs/TEST_MATRIX.md`, including two-monitor display selection, are owed on real
  hardware.
