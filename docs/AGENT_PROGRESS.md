# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01 is done (G01 PASS, confirmed on CI run 40201274900). P02-01 is done; P02-02..P02-05 are implemented and **G02 waits for the owner's hardware matrix**. P03 is in progress with owner approval; P03-01, P03-02 and P03-03 are implemented (awaiting manual validation).
- **Active task:** next is P03-04 (first-run onboarding and sign-in startup). P03-03 (settings window; ADR 0014) is IMPLEMENTED_AWAITING_VALIDATION. G02 and the P03 manual rows (Y1–Y7, TA1–TA10, SW1–SW8) are owed by the owner.
- **Active task status:** P03-03 IMPLEMENTED_AWAITING_VALIDATION (2026-10-26), **uncommitted** (the owner commits). P03-02 is committed as `ba52b9e`, and CI 40286979868 is green with 0 skipped.
- **Last passed gate:** G01 (2026-10-25; CI run https://github.com/imamulkadir/DyanamicEye/actions/runs/40201274900). G00: CI run 40196393380.
- **Repository:** https://github.com/imamulkadir/DyanamicEye (public; default branch `master`).
- **Commits:** **the owner reviews and makes all commits; agents must NOT commit.** List the changed files and a suggested message instead.
- **CI:** the `gh` CLI is authenticated as imamulkadir but is not on the agent's PowerShell PATH, so use `& "C:\Program Files\GitHub CLI\gh.exe"`.
  - `run list --repo imamulkadir/DyanamicEye -L 3`
  - `run view <id> --repo imamulkadir/DyanamicEye --log-failed`
  - `run watch <id> --repo imamulkadir/DyanamicEye --exit-status`

  The CI runner (2 vCPU) is slower than the dev box, so avoid real-timer assertions and fire-and-forget IPC.
- **Host:** Windows 11 Pro 25H2 build 26200.9711, x64. SDK 10.0.401 (pinned in `global.json`); runtimes 10.0.12; 6 CPUs.
- **Supported OS:** ADR 0001. The tested target is Win11 24H2/25H2 x64. Win10 22H2 Home/Pro is NOT supported by .NET 10.
- **Build/test** (Microsoft.Testing.Platform via `global.json`):
  - `dotnet restore DynamicIsland.sln --locked-mode`
  - `dotnet build DynamicIsland.sln -c Release --no-restore` and `dotnet test --solution DynamicIsland.sln -c Release --no-build --max-parallel-test-modules 1`
  - the same two with `-c Debug`. Debug compiles the test hooks and runs the crash-injection smoke tests.

  The smoke tests start the real app and launcher on the desktop, and they assume no other Dynamic Island instance is running.
- **Last updated:** 2026-10-26 (P03-03 IMPLEMENTED_AWAITING_VALIDATION; next P03-04)

## NEXT ACTION (ONE task only)
**Owner first:** review and commit the P03-03 changes (file list in RECENT VERIFIED CHANGES), push, and check that CI is green. Optionally run manual rows SW1–SW8.
**Then agent: start P03-04** (guide §5 P03-04): first-run onboarding that asks for sign-in startup permission and shows privacy and widget choices.
- Startup uses HKCU only, with the stable launcher path and a 5 s default delay; a disabled startup is obeyed.
- Write the consents (`GeneralSettings.StartWithWindows`, `UpdateSettings.AutomaticChecks`) only from an explicit user choice.
- Replace the read-only consent text on the settings General and Updates pages (ADR 0014 §6) with real controls tied to their effect.
- Write an ADR for the Run-entry format and the delay mechanism before implementing.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | DONE | **G01 PASS** 2026-10-25, CI run 40201274900 (Release + Debug). See the G01 evidence below. |
| P02 — Island shell | P02-01…P02-05 | IN_PROGRESS (01 DONE; 02–05 IMPLEMENTED_AWAITING_VALIDATION) | G02 pending (needs a real multi-DPI/multi-monitor check) |
| P03 — Tray/settings/startup | P03-01…P03-05 | IN_PROGRESS (01–03 IMPLEMENTED_AWAITING_VALIDATION; 04 next; started with owner approval while G02 is open) | G03 pending |
| P04 — Core widgets/data | P04-01…P04-07 | NOT_STARTED | G04 pending |
| P05 — Customization/performance | P05-01…P05-04 | NOT_STARTED | G05 pending |
| P06 — Packaging/updates | P06-01…P06-09 | NOT_STARTED | G06 pending |
| P07 — Release validation | P07-01…P07-05 | NOT_STARTED | G07 pending |

### G01 evidence (guide §5: "corrupt settings recover; simulated widget failure isolated; deliberate Exit never auto-relaunches; third crash does not loop; no duplicate app")
| Criterion | Proof |
|---|---|
| Corrupt settings recover | `JsonSettingsStoreTests` (12 corrupt variants, backup recovery); real app run with a corrupt file logs `ResetAfterCorruption` (P01-03, WORK_LOG) |
| Simulated widget failure isolated | `ComponentBoundaryTests.Failing_component_is_isolated_while_others_keep_running`: the faulty component ends `Failed` after 1+3 runs, the other stays `Healthy`, no shutdown is requested, and a Warning notice is published |
| Deliberate Exit never auto-relaunches | `AppSupervisorTests.Clean_exit_is_never_relaunched`; smoke `Deliberate_exit_through_the_launcher_is_never_relaunched` with the real launcher and app |
| Third crash does not loop | `AppSupervisorTests.Crash_loop_…`; Debug end-to-end smoke `Crash_loop_restarts_twice_tries_safe_mode_once_then_stops`, also green on CI run 40200547110 |
| No duplicate app | Smoke `Second_launch_exits_quietly_and_leaves_the_first_instance_running`; `SingleInstanceGuardTests` |

## RECENT VERIFIED CHANGES (newest first; max 10; older detail in `docs/WORK_LOG.md`)
- 2026-10-26 **P03-03 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0014**.
  - **Core:**
    - New `Shell/SettingsEditor.cs`: the settings-window draft (INotifyPropertyChanged), strict validation with user messages using `SettingsValidator` limits, `IsDirty`/`CanSave`, per-page and full reset (consents are never reset), widget move, and `CreateChange()`/`ApplyTo()`, which merge only the fields the user changed onto the latest settings. Loading suppresses intermediate notifications.
    - `TrayMenu.cs`: `OrderDisplays` is shared with the settings display list, and Settings… is now enabled.
  - **App:**
    - New `SettingsWindow.xaml(.cs)` with 8 left tabs (General, Appearance, Behavior, Widgets, Updates, Privacy, Diagnostics, About).
      - Buttons: Save (default, Ctrl+S), Revert, Close (Esc).
      - Closing with unsaved changes asks Save / Don't save / Cancel. The prompt is skipped while the app shuts down or with the NoDialogs test hook.
      - A banner shows when the store is read-only. A status line (UIA live region) names the page that holds an error.
      - Diagnostics shows the `DiagnosticBundleBuilder` preview, then Save report… (a save dialog, then `WriteZipAsync`).
      - Privacy shows the data folder with an Open button. About links to the GitHub page (https only).
      - Consents are shown as read-only text.
    - New `SettingsWindowService` (one window: open or activate), `AppInfo` (version, copyright, settings load outcome for diagnostics) and `ShellLauncher` (open a folder or an https link; the tray uses it too).
    - `AppHost` and `TrayController` wiring; `App.xaml.cs` uses `AppInfo`.
    - `AppLog` adds SettingsSaved (1046), DiagnosticReportSaved (1047), DiagnosticReportFailed (1048) and OpenFolderFailed (1049).
  - **Tests:**
    - Unit: `Shell/SettingsEditorTests.cs` (31 cases) and the display-order and settings-enabled tray tests.
    - Integration: `SettingsWindowTests.cs` (9, real offscreen WPF window). Covers pages and AutomationIds; every input has a UIA name (this found 10 `Label.Target` inputs without names, now fixed); save merges with a concurrent tray change; reload without edits; validation disables Save; a failed save keeps the draft; read-only banner; display list; the diagnostic preview writes no file.
    - Smoke: `Settings_opens_once_and_closing_it_keeps_the_app_running` (real app, tray → Settings… twice, WM_CLOSE, the app stays up and exits 0).
  - **Bug found and fixed:** `Load` reported a half-loaded draft (widgets cleared) as dirty, which cleared the "Settings saved." status.
  - **Docs:** ADR 0014; TEST_MATRIX rows SW1–SW8. The P03-02 tray rows were renamed to TA1–TA10 because T1/T2 were already used by G02, and the ADR 0013 reference was updated.
  - **Changed files for the owner:**
    - modified: `docs/ADR/0013-tray-actions-settings-restart.md`, `docs/ADR/README.md`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md`, `docs/WORK_LOG.md`, `src/DynamicIsland.App/{App.xaml.cs, AppHost.cs, AppLog.cs, TrayController.cs}`, `src/DynamicIsland.Core/Shell/TrayMenu.cs`, `tests/DynamicIsland.UnitTests/Shell/TrayMenuModelTests.cs`, `tests/DynamicIsland.WindowsSmokeTests/TrayActionSmokeTests.cs`
    - new: `docs/ADR/0014-settings-window.md`, `src/DynamicIsland.App/{AppInfo.cs, SettingsWindow.xaml, SettingsWindow.xaml.cs, SettingsWindowService.cs, ShellLauncher.cs}`, `src/DynamicIsland.Core/Shell/SettingsEditor.cs`, `tests/DynamicIsland.IntegrationTests/SettingsWindowTests.cs`, `tests/DynamicIsland.UnitTests/Shell/SettingsEditorTests.cs`
    - Suggested commit message: `feat(settings): settings window with explicit save, validation and reset (P03-03, ADR 0014)`
- 2026-10-26 P03-02 committed by the owner as `ba52b9e` and pushed; CI run 40286979868 **success** (0 skipped).
- 2026-10-26 **P03-02 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits).
  - Wrote ADR 0013 (`docs/ADR/0013-tray-actions-settings-restart.md`, indexed in the ADR README) and TEST_MATRIX rows T1–T10.
  - Intermittent smoke failures: neither reproduced in 7 full-suite runs (see TEST EVIDENCE). Analysis:
    - (a) The fullscreen failure took about 10 s. That means the helper window did become foreground (otherwise the test skips at 5 s), but the island did not hide within the next 5 s. The cause is unknown; the test captures the app log tail if it fails again.
    - (b) The fullscreen detector's out-of-context `EVENT_OBJECT_LOCATIONCHANGE` hook wakes the app for every window or cursor move anywhere on the desktop. It returns early, but idle CPU still depends on other activity on the screen. This dates from P02-04 and is not specific to P03-02. Limit left at 300 ms. Possible later refinement: install the location hook only for the current foreground window's process/thread.
  - **Changed files for the owner (since P03-01):**
    - modified: `docs/AGENT_PROGRESS.md`, `docs/ADR/README.md`, `docs/TEST_MATRIX.md`, `src/DynamicIsland.App/{App.xaml, App.xaml.cs, AppHost.cs, AppLog.cs, FullscreenCoordinator.cs, IslandWindow.xaml.cs, MotionGate.cs, TrayController.cs}`, `src/DynamicIsland.Core/Components/{ComponentBoundary.cs, ComponentHost.cs}`, `src/DynamicIsland.Core/Lifecycle/{AppSupervisor.cs, ExitReason.cs}`, `src/DynamicIsland.Launcher/{ProcessAppRunner.cs, Program.cs}`, `src/DynamicIsland.Platform.Windows/Tray/Win32TrayIcon.cs`
    - deleted: `src/DynamicIsland.App/AppSettingsSnapshot.cs`
    - new: `docs/ADR/0013-tray-actions-settings-restart.md`, `src/DynamicIsland.App/RestartService.cs`, `src/DynamicIsland.Core/Lifecycle/LauncherArguments.cs`, `src/DynamicIsland.Core/Settings/SettingsService.cs`, `src/DynamicIsland.Core/Shell/TrayMenu.cs`, `tests/DynamicIsland.UnitTests/{Lifecycle/RestartContractTests.cs, Settings/SettingsServiceTests.cs, Shell/TrayMenuModelTests.cs}`, `tests/DynamicIsland.WindowsSmokeTests/{TrayActionSmokeTests.cs, TrayMenuDriver.cs}`
    - Suggested commit message: `feat(tray): tray actions, settings service, restart contract (P03-02, ADR 0013)`
  - **Decisions (ADR 0013):**
    - **Exit codes:** `ExitReason.RestartInSafeMode` with **exit code 4**. The launcher relaunches in Safe Mode on 4 and normally on 3; neither counts as a crash, and both share the existing cap of 5 restart requests per launcher lifetime.
    - **Launcher contract** (`Core/Lifecycle/ExitReason.cs` → `LauncherContract`):
      - The launcher sets the env var `DYNAMICISLAND_SUPERVISED=1` for its child.
      - The launcher accepts the allow-listed `--safe-mode` and `--after-pid N` (`Core/Lifecycle/LauncherArguments.cs`). With `--after-pid` it waits up to 15 s for that process, so the single-instance lock is free.
    - **Restart** (`App/RestartService.cs`): when supervised, the app exits with 3/4. When started directly, it starts the launcher with `--after-pid <own pid> [--safe-mode]`, then exits. Without a launcher next to the app the restart is refused and logged.
    - **Settings:** `Core/Settings/SettingsService.cs` (`ISettingsService`: `Current`, `IsReadOnly`, `Changed`, serialized `UpdateAsync` = change → validate → save → publish; a read-only store applies the change for the session only; a failed save keeps the old settings). It replaces `AppSettingsSnapshot`, which was **deleted**. `IslandWindow` re-places itself and re-applies its state on `Changed`.
    - **Shutdown:** `App.xaml` sets `ShutdownMode="OnExplicitShutdown"`. Closing the island window (Alt+F4 or a close request) is turned into `RequestShutdown(UserExit)` in its `Closing` handler, so future windows such as Settings can close freely.
    - **Pause:** tray Pause → state machine `Paused` → `ComponentHost.SetPausedAsync(true)` stops optional components (status Stopped, reason `paused`); Resume restarts them. `ComponentBoundary` is now restartable after a stop.
    - **Tray menu:** a pure model `Core/Shell/TrayMenu.cs` (`TrayMenuModel.Build`) with Show/Hide, Pin (enabled only while visible), Pause/Resume, a Display submenu ("Display N (primary)" ordered left→right, current monitor checked, monitor ids only as arguments), Reset to primary display (also resets offsets: the lost-island recovery), Settings… and Check for updates (both **disabled with tooltips** until P03-03/P06), Open logs folder, Restart in/Leave Safe Mode, Restart and Exit. Every item has an AutomationId `Tray.*`.
    - `TrayController` was rewritten to render the model and execute commands (settings updates are async with failures logged, never crashing).
  - **Other changes:**
    - `AppLog` adds RestartRequested, RestartUnavailable, SettingsNotSaved, OpenLogsFailed and ComponentPauseIncomplete.
    - `AppHost` registrations: `SettingsService`, `RestartService`, and the `TrayController` factory with the logs folder.
    - `App.xaml.cs` wires pause to the component host and initializes the settings service after load.
    - `Win32TrayIcon.RequestContextMenu`.
  - **Tests added:**
    - `UnitTests/Shell/TrayMenuModelTests.cs`
    - `UnitTests/Settings/SettingsServiceTests.cs`
    - `UnitTests/Lifecycle/RestartContractTests.cs`: launcher args, exit codes, Safe Mode restart, start in Safe Mode, component pause/resume
    - smoke `TrayMenuDriver.cs`: opens the real tray menu via the callback message and invokes items by AutomationId with UIA, no mouse
    - smoke `TrayActionSmokeTests.cs`: pause/resume (all configs); reset display saves settings, restart through the launcher (3 → new instance, 4 → Safe Mode), and unsupervised restart handover (Debug-only, isolated data root)
- 2026-10-25 P03-01 pushed; CI run 40208475923 **success**: Release 350, Debug 351, 0 skipped.
- 2026-10-25 **Owner approval:** "pushed, proceed" after G02 was set up for owner validation. Recorded as approval to **continue P03 while G02 stays open**; the hardware matrix rows are still owed.
- 2026-10-25 **P03-01 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; owner commits). Decisions are in ADR 0012.
  - **Platform.Windows:**
    - `Tray/Win32TrayIcon.cs`: `Shell_NotifyIcon` v4, select/keyselect/contextmenu events, `TaskbarCreated` re-add, DPI-sized icon from the exe (group 32512) via `LoadImageW` with a system-icon fallback, `IsRegistered`, `PrepareForMenu`, `SendTaskbarCreated`.
    - `IslandWindowInterop.FindTopLevelWindow` and `RequestClose`.
    - New interop.
  - **App:**
    - New `TrayController.cs`: hidden top-level owner `HwndSource`, accessible WPF menu (Show/Hide, Exit), left-click toggle, `WM_CLOSE` treated as Exit.
    - `AppHost` registration; `App.xaml.cs` starts it (disposed with the host); `AppLog.TrayUnavailable`.
  - **Tests:**
    - `IntegrationTests/Win32TrayIconTests.cs`: real registration, simulated Explorer restart, removal; v4 event mapping.
    - Smoke `TraySmokeTests.cs`: real app icon, `TaskbarCreated` recovery, removed on exit, and the WM_CLOSE regression.
    - `IslandPlacementSmokeTests` click-through now waits for the first paint.
  - **Bugs found:**
    - (1) `taskkill` closed the hidden tray window, so the app kept running with an orphaned icon. Fixed and regression-tested.
    - (2) The intermittent Debug click-through failure was a race with the first WPF paint of the layered window. Fixed in the test, by waiting for the painted state.
    - Known limitation: a close request while the tray menu is open only dismisses the menu.

## TEST EVIDENCE (current task; older rows in `docs/WORK_LOG.md`)
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P03-01 | GitHub Actions `windows-2025` | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40208475923 |
| 2026-10-25 | P03-02 | Windows 11 Pro 25H2 x64, 1 monitor at 100 % | `dotnet build` Release + Debug | PASS | 0 warnings (earlier attempts failed on IDE0005; fixed). One Debug build hit a transient "temporary files folder is full / insufficient resources" MSBuild error: disk 27 GB free, RAM 7.9 GB free, no leaks found; the rebuild passed. |
| 2026-10-25 | P03-02 | same | Unit tests | PASS | 322/322 |
| 2026-10-25 | P03-02 | same | Smoke project alone: Debug 18 tests ×4, Release 14 ×4 | PASS (some skipped) | All tray actions pass, including restart 3/4 and Safe Mode. The fullscreen test was **skipped** in some runs (Windows refused foreground). |
| 2026-10-26 | P03-03 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then `dotnet test --solution DynamicIsland.sln -c <cfg> --no-build --max-parallel-test-modules 1`: Release ×2, Debug ×2 | PASS | 0 failed in all runs. Release 421/421 once, and 420 + 1 skipped once (the fullscreen smoke test: Windows refused the helper window foreground, so a skip, not a pass). Debug 425/425 twice. Offscreen renderings of all 8 pages were checked by eye: layout, error message, banner. |
| 2026-10-26 | P03-02 | GitHub Actions `windows-2025` (`ba52b9e`) | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40286979868; Release 380/380, Debug 384/384, **0 skipped** (fullscreen and idle-CPU smoke tests both ran and passed) |
| 2026-10-26 | P03-02 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug, then `dotnet test --solution DynamicIsland.sln -c <cfg> --no-build --max-parallel-test-modules 1`: Release ×4, Debug ×3 | PASS | 0 failed in all 7 runs. Release: 380/380 three times and 379 + 1 skipped once. Debug: 384/384 twice and 383 + 1 skipped once. Both skips were the fullscreen smoke test, skipped because Windows refused the helper window foreground; this is a skip, not a pass. The intermittent failures from 2026-10-25 did not reproduce. |
| 2026-10-25 | P03-02 | same, `CI=true` | Full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | **2 intermittent FAILs** | Release run 1: fullscreen smoke failed. Debug run 1: idle CPU over 300 ms. Runs 2: Release 380 (1 skipped), Debug 384 green. Not yet diagnosed; see NEXT ACTION. |
| 2026-10-25 | P02-05 | GitHub Actions `windows-2025` | CI workflow (Release + Debug, sequential modules) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40207131071; 342/343, 0 skipped |
| 2026-10-25 | P03-01 | Windows 11 Pro 25H2 x64, 1 monitor at 100 % | Integration 44/44 and smoke 12/12 (Release), first run | PASS | Tray registered, recovered after a simulated `TaskbarCreated`, removed on exit; 0 skipped |
| 2026-10-25 | P03-01 | same, real desktop | Opened the tray menu via the callback message (no synthetic mouse) and took a screenshot; then `taskkill` | FAIL → fixed | The menu shows "Hide island" and "Exit" with focus on the first item. `taskkill` left the app running with the tray window destroyed. Fixed: WM_CLOSE on the tray window means Exit; `taskkill` now exits with code 0. |
| 2026-10-25 | P03-01 | same, `CI=true` | `dotnet test --solution … --max-parallel-test-modules 1`, Release and Debug ×2 | PASS | Release 350/350, Debug 351/351, 0 skipped, no stray processes. One earlier Debug run failed the click-through test (first-paint race); fixed in the test. |
| — | P03-01 | real Explorer restart, keyboard, high DPI | `TEST_MATRIX.md` rows Y1–Y7 | PENDING_WINDOWS | Owner |
| 2026-10-25 | P02-04 | GitHub Actions `windows-2025` | CI workflow | **FAIL** | https://github.com/imamulkadir/DyanamicEye/actions/runs/40205375218: `Island_hides_while_a_fullscreen_window_is_in_front_and_returns_afterwards`, 329/330 |
| 2026-10-25 | P02-03 notch | GitHub Actions `windows-2025` | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40204446036 |
| 2026-10-25 | P02-02 | GitHub Actions `windows-2025` | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40202761834 |
| 2026-10-25 | P02-01 | GitHub Actions `windows-2025` | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40201946771 |
| — | P02-01 | multi-monitor, mixed DPI, 125–200 % | Placement on a secondary or high-DPI monitor, fallback when unplugged | PENDING_WINDOWS | Single 100 % monitor on the dev host; covered by unit tests; G02 matrix in P02-02 || 2026-10-25 | P01-04 | GitHub Actions `windows-2025`, owner push | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40200547110 |

## BLOCKERS / RISKS
- Enterprise policy may block an unsigned per-user executable; never bypass it.
- Self-update and rollback must handle a read-only portable folder and DB schema compatibility (P06).
- The dev host is one security patch behind (runtime 10.0.12 / SDK 10.0.401 vs 10.0.13 / 10.0.402). The owner should update before any release build.
- Packaging (P06) must ship `DynamicIsland.Launcher.exe` next to `DynamicIsland.exe`, and shortcuts and startup target the launcher (ADR 0006).
- G02 requires multi-monitor, DPI 100–200 %, unplug/rotate and sleep/wake checks on real hardware. The dev host has one monitor visible to the agent, so the owner may need to run part of that matrix.

## ARCHITECTURE DECISIONS / ADRS
- Frozen D01–D15: see guide §0. No deviations approved.
- 0001 supported Windows versions · 0002 solution/build/test platform · 0003 settings persistence (consent defaults `NotAsked`; schema-bump rule for new enum values) · 0004 local logging and redaction · 0005 lifecycle, exit codes 0/1/3, single instance with acknowledged pipe · 0006 crash supervision, Safe Mode, Debug-only test hooks, launcher as the entry point · 0007 component error boundaries, notices (keys only), user-reviewed diagnostic export · 0008 island window (tool window + PerMonitorV2), monitor identity (device interface path, never logged), placement calculator, display-change debounce · 0009 state machine (exhaustive table), fixed-size window with animated island, motion preference, keyboard/focus return, UI Automation patterns; amended: notch design (flush top, shoulders, sensor housing) · 0010 fullscreen detection (WinEvent hooks, covers-monitor rule, per-monitor policy, exe-name exceptions, honest exclusive-fullscreen limit; amended: uncloak hook + one-shot follow-up) · 0011 idle activity budget, power-aware motion policy, offscreen visual regression with own references, sequential test modules · 0012 Win32 tray icon (v4 events, TaskbarCreated, DPI icon, WM_CLOSE → Exit).
- Pending: per-user installer technology and updater trust model (P06).

## HANDOFF NOTES
- Read `AGENTS.md` first; guide §5 has the task definitions. Current next task: **finish P03-02 per NEXT ACTION** (G02 still owed by the owner).
- Widgets and providers (P04) must be `IIsolatedComponent`s registered as optional (ADR 0007). They must honor Safe Mode (ADR 0006) and never log free text (ADR 0004).
- If code is present that this file does not describe, inspect git and tests and reconcile this file before editing.
