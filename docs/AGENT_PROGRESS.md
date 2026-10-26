# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01 is done (G01 PASS, confirmed on CI run 40201274900). P02-01 is done; P02-02..P02-05 are implemented and **G02 waits for the owner's hardware matrix**. P03 is in progress with owner approval; P03-01…P03-05 are implemented (awaiting manual validation); **G03 waits for the owner's hardware checks**. P04 started with owner approval while G02/G03 stay open.
- **Active task:** next is P04-03 (media widget). P04-02 (clock/date widget; ADR 0019) is IMPLEMENTED_AWAITING_VALIDATION. G02/G03 manual rows are still owed by the owner.
- **Active task status:** P04-02 IMPLEMENTED_AWAITING_VALIDATION (2026-10-26), **uncommitted** (the owner commits). P04-01 is committed as `294a8f0`, and CI 40301030072 is green with 0 skipped.
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
- **Last updated:** 2026-10-26 (P04-02 IMPLEMENTED_AWAITING_VALIDATION; next P04-03)

## NEXT ACTION (ONE task only)
**Owner first:** review and commit the P04-02 changes (file list in RECENT VERIFIED CHANGES), push, and check that CI is green.
**Then agent: P04-03** (guide §5): the media widget.
- Use the global Windows media sessions (`GlobalSystemMediaTransportControlsSessionManager`, WinRT) behind a Core `IMediaSessionService` (guide §3) implemented in Platform.Windows.
- Handle no session, several players, stale sessions, and controls a player does not support.
- Load album art safely: size cap, decode off the UI thread, no file paths in logs. Track titles are personal data: never log them.
- Check the current official docs for the WinRT projection on .NET 10 (target framework `net10.0-windows10.0.x`) and its packaging impact before choosing. Write an ADR first.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | DONE | **G01 PASS** 2026-10-25, CI run 40201274900 (Release + Debug). See the G01 evidence below. |
| P02 — Island shell | P02-01…P02-05 | IN_PROGRESS (01 DONE; 02–05 IMPLEMENTED_AWAITING_VALIDATION) | G02 pending (needs a real multi-DPI/multi-monitor check) |
| P03 — Tray/settings/startup | P03-01…P03-05 | IN_PROGRESS (01–05 IMPLEMENTED_AWAITING_VALIDATION; started with owner approval while G02 is open) | G03 pending (owner hardware checks) |
| P04 — Core widgets/data | P04-01…P04-07 | IN_PROGRESS (01–02 IMPLEMENTED_AWAITING_VALIDATION; 03 next; started with owner approval while G02/G03 are open) | G04 pending |
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
- 2026-10-26 **P04-02 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0019**; ADR 0016 §4/§6 are amended. No countdown timers or stopwatch: they were offered to the owner, who did not ask for them (guide: "timers only if approved").
  - **Core:** `IWidget.SetActive`/`OnSystemChanged` (default no-ops); `WidgetRegistry.SetActive/NotifySystemChanged/Dispose`.
  - **Widgets:**
    - New `ClockWidget` + `ClockSnapshot`: ticking on the `TimeProvider` aligned to the minute or second, waking on settings, activation and system changes; inactive means no ticks. Uses `ClockFormatter` (ADR 0016) and the long date.
    - `BuiltInWidgets.Create(settings, time)` returns the clock.
  - **App:**
    - **`NotchClock.cs` deleted.** `WidgetPresenter` now sets the notch text from the clock slot (blank when off, failed or in Safe Mode) and renders the expanded clock view (34 px light time plus the long date).
    - The presenter maps island visibility to `SetActive` and forwards `SystemEvents` (time, locale, resume).
    - `AppHost`/`App.xaml.cs` wiring updated.
  - **Tests:**
    - **`NotchClockTests.cs` deleted.**
    - Unit: `Widgets/ClockWidgetTests.cs` (6, real time, bounded): immediate publish with the options and date; at most 2 publishes in 1.5 s by default; 3–5 in 2.5 s with seconds; inactive means no ticks, then catch-up; settings and system changes refresh at once; stop and release.
    - Integration: `ClockWidgetPresenterTests` (real registry, widget, presenter and NotchView): the notch shows the compact text, the panel shows the time and date, and disabling clears both.
    - An expanded rendering was checked by eye.
  - **Process note:** while removing the two files I ran `git rm`, which staged the deletions against the no-git-writes rule. I immediately unstaged them with `git reset -q -- <the two paths>`; the index again matches HEAD, and the files are only deleted in the working tree.
  - **Changed files for the owner:**
    - modified: `docs/ADR/{0016-notch-clock.md, README.md}`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md` (CW1–CW3), `docs/WORK_LOG.md` (older entries archived), `src/DynamicIsland.App/{App.xaml.cs, AppHost.cs, WidgetPresenter.cs}`, `src/DynamicIsland.Core/Widgets/WidgetRegistry.cs`, `src/DynamicIsland.Widgets/BuiltInWidgets.cs`
    - deleted: `src/DynamicIsland.App/NotchClock.cs`, `tests/DynamicIsland.IntegrationTests/NotchClockTests.cs`
    - new: `docs/ADR/0019-clock-widget.md`, `src/DynamicIsland.Widgets/ClockWidget.cs`, `tests/DynamicIsland.IntegrationTests/ClockWidgetPresenterTests.cs`, `tests/DynamicIsland.UnitTests/Widgets/ClockWidgetTests.cs`
    - Suggested commit message: `feat(widgets): clock and date widget drives notch and expanded view (P04-02, ADR 0019)`
- 2026-10-26 P04-01 committed by the owner as `294a8f0`; CI run 40301030072 **success**: Release 533/533, Debug 540/540, 0 skipped.
- 2026-10-26 **Owner approval:** after P03-05 was pushed, the reply "pushed, continue" (to the question "run G02/G03 or approve continuing to P04") is recorded as **approval to start P04 while G02/G03 stay open**.
- 2026-10-26 **P04-01 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0018**.
  - **Core:**
    - `Components/ComponentHost`: per-component enable/disable.
      - `Add(..., enabled)`, `SetEnabledAsync` (bounded stop, then status Stopped/"disabled"), `StatusOf`.
      - Pause/resume and enable/disable are serialized; Resume never starts disabled components.
      - Every non-skipped component is registered for shutdown once.
    - `ComponentBoundary.IsRunning/MarkDisabled`.
    - New `Widgets/WidgetRegistry.cs`:
      - `IWidget` (an `IIsolatedComponent` with `Id`, `Snapshot`, `SnapshotChanged`, `Release`) and `WidgetSlot`.
      - `WidgetRegistry` registers widgets with the saved enabled flags; `ApplyAsync` handles enable, disable plus `Release` (3 s stop bound) and reorder; ordered `Slots`; `Changed`; optional retry policy.
  - **Widgets:** new `BuiltInWidgets.Create`, the compiled-in catalog. It is empty in P04-01; P04-02…06 add their modules.
  - **App:**
    - `NotchView.ShowWidgets`: slots in order; a failed widget shows "<name> isn't available right now."; widgets without content take no space; the hint stays when nothing is shown; AutomationId `Widget.<id>`.
    - New `WidgetPresenter`: applies settings changes off the UI thread and coalesces redraws on the UI thread; per-widget views in `Views`.
    - `AppLog` 1058 WidgetChangeIncomplete.
    - `AppHost`/`App.xaml.cs` wiring: register before `StartAll`, presenter after.
  - **Tests:**
    - Unit: `Widgets/WidgetRegistryTests.cs` (8). Covers: order and unknown ids; disabled never starts; disable cancels the work, releases content and restarts on enable; reorder without restart; failure isolation with an "unavailable" slot; pause/resume with enable while paused; Safe Mode; id/name validation.
    - Integration: `WidgetPanelTests.cs` (2).
  - **No visible change yet:** there are no widget modules until P04-02.
  - **Changed files for the owner:**
    - modified: `docs/ADR/README.md`, `docs/AGENT_PROGRESS.md`, `src/DynamicIsland.App/{App.xaml.cs, AppHost.cs, AppLog.cs, NotchView.xaml, NotchView.xaml.cs}`, `src/DynamicIsland.Core/Components/{ComponentBoundary.cs, ComponentHost.cs}`
    - new: `docs/ADR/0018-widget-registry.md`, `src/DynamicIsland.App/WidgetPresenter.cs`, `src/DynamicIsland.Core/Widgets/WidgetRegistry.cs`, `src/DynamicIsland.Widgets/BuiltInWidgets.cs`, `tests/DynamicIsland.IntegrationTests/WidgetPanelTests.cs`, `tests/DynamicIsland.UnitTests/Widgets/WidgetRegistryTests.cs`
    - Suggested commit message: `feat(widgets): widget registry and lifecycle with enable/disable (P04-01, ADR 0018)`
- 2026-10-26 P03-05 committed by the owner as `2dc4315`; CI run 40299277622 **success**: Release 523/523, Debug 530/530, 0 skipped.
- 2026-10-26 **P03-05 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0017**.
  - **Core:**
    - New `Shell/Hotkey.cs`: `Hotkey.TryParse`/`ToString`/`VirtualKey` (keys A–Z, 0–9, F1–F24, Space; **at least 2 modifiers**; canonical text "Ctrl+Alt+Shift+Win+Key"; `HotkeyModifiers` = Win32 MOD_* values).
    - `ShortcutAction.For/Opens`: Hidden → Show+Click; Paused → Resume+Click; Collapsed/Peek → Click; Expanded/Pinned → Escape; fullscreen → nothing.
    - `BehaviorSettings.Shortcut` (`ShortcutSettings`: `Enabled` false, `Keys` "Ctrl+Alt+I"). Additive, no schema bump. `BehaviorSettings` equality includes it.
    - `SettingsValidator` canonicalizes the keys or falls back to the default keys.
    - `SettingsEditor`: `ShortcutEnabled`/`ShortcutText`/`ShortcutError` (merged per field, reset with Behavior).
  - **Platform.Windows:**
    - New `Input/Win32Hotkey.cs`: `RegisterHotKey` with MOD_NOREPEAT; `Registered`/`InUse` (error 1409)/`Failed`.
    - `NativeMethods` Register/UnregisterHotKey; `IslandWindowInterop.FindMessageWindow` and `Post`.
  - **App:**
    - New `ShortcutController`: a message-only window, registers only when enabled, re-registers on change, and activates the island before expanding.
    - `AppLog` 1056/1057, without the key combination.
    - `AppHost`/`App.xaml.cs` wiring.
    - `SettingsWindow` Behavior: a check box, a shortcut field with validation, and the registration status (on / in use / failed / off).
  - **Tests:**
    - Unit: `Shell/HotkeyTests.cs` (35 cases: parsing, rules, virtual keys, actions end Expanded, validator) and an editor shortcut test.
    - Integration: `Win32HotkeyTests` (a real registration, a conflict from a second window reported as InUse, release) and a settings window shortcut test.
    - Smoke (Debug): `ShortcutSmokeTests`. With the shortcut enabled in isolated settings, a second registration from the test is refused (proof that Windows granted it to the app). A posted WM_HOTKEY opens the island ("Dynamic Island, expanded") and the next one closes it. Released on exit.
  - **Docs:** ADR 0017 (indexed); TEST_MATRIX SC1–SC4.
  - **Changed files for the owner:**
    - modified: `docs/ADR/README.md`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md`, `docs/WORK_LOG.md` (P03-01/02 entries archived), `src/DynamicIsland.App/{App.xaml.cs, AppHost.cs, AppLog.cs, SettingsWindow.xaml, SettingsWindow.xaml.cs}`, `src/DynamicIsland.Core/Settings/{AppSettings.cs, SettingsValidator.cs}`, `src/DynamicIsland.Core/Shell/SettingsEditor.cs`, `src/DynamicIsland.Platform.Windows/{Display/IslandWindowInterop.cs, NativeMethods.cs}`, `tests/DynamicIsland.IntegrationTests/SettingsWindowTests.cs`, `tests/DynamicIsland.UnitTests/Shell/SettingsEditorTests.cs`
    - new: `docs/ADR/0017-global-shortcut.md`, `src/DynamicIsland.App/ShortcutController.cs`, `src/DynamicIsland.Core/Shell/Hotkey.cs`, `src/DynamicIsland.Platform.Windows/Input/Win32Hotkey.cs`, `tests/DynamicIsland.IntegrationTests/Win32HotkeyTests.cs`, `tests/DynamicIsland.UnitTests/Shell/HotkeyTests.cs`, `tests/DynamicIsland.WindowsSmokeTests/ShortcutSmokeTests.cs`
    - Suggested commit message: `feat(shortcut): optional global shortcut via RegisterHotKey (P03-05, ADR 0017)`

## TEST EVIDENCE (current task; older rows in `docs/WORK_LOG.md`)
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P03-01 | GitHub Actions `windows-2025` | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40208475923 |
| 2026-10-25 | P03-02 | Windows 11 Pro 25H2 x64, 1 monitor at 100 % | `dotnet build` Release + Debug | PASS | 0 warnings (earlier attempts failed on IDE0005; fixed). One Debug build hit a transient "temporary files folder is full / insufficient resources" MSBuild error: disk 27 GB free, RAM 7.9 GB free, no leaks found; the rebuild passed. |
| 2026-10-25 | P03-02 | same | Unit tests | PASS | 322/322 |
| 2026-10-25 | P03-02 | same | Smoke project alone: Debug 18 tests ×4, Release 14 ×4 | PASS (some skipped) | All tray actions pass, including restart 3/4 and Safe Mode. The fullscreen test was **skipped** in some runs (Windows refused foreground). |
| 2026-10-26 | P04-02 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS (2 skipped) | Debug 541/541 ×2. Release 533 passed + **1 skipped** ×2: the fullscreen smoke test, because Windows refused the helper window foreground; that is a skip, not a pass. Run alone afterwards it **passed 2/2** (`--filter-class FullscreenSmokeTests`). The desktop was in interactive use at the time; another DynamicIsland Debug process (not started by the tests) briefly locked the build output. |
| 2026-10-26 | P04-01 | GitHub Actions `windows-2025` (`294a8f0`) | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40301030072; 533/533 and 540/540, 0 skipped |
| 2026-10-26 | P04-01 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2; unit tests alone ×3 (the registry tests are timing-based) | PASS | Release 533/533 ×2, Debug 540/540 ×2, **0 skipped**; unit 441/441 ×3 |
| 2026-10-26 | P03-05 | GitHub Actions `windows-2025` (`2dc4315`) | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40299277622; 523/523 and 530/530, 0 skipped |
| 2026-10-26 | P03-05 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS | Release 523/523 ×2, Debug 530/530 ×2, **0 skipped** |
| 2026-10-26 | P03-04 + clock | GitHub Actions `windows-2025` (`8917a21`) | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40296571628; 488/488 and 494/494, 0 skipped |
| 2026-10-26 | notch clock | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS | Release 488/488 ×2, Debug 494/494 ×2, **0 skipped** (including the idle-CPU budget and the regenerated visual references). A real Release app on the desktop showed "2:54 PM" and exited 0. |
| 2026-10-26 | P03-04 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS | Release 465/465 ×2 and Debug 471/471 ×2, **0 skipped**. Real HKCU Run key, test keys and real settings checked untouched afterwards. Offscreen renderings of onboarding and the General page checked by eye. |
| 2026-10-26 | P03-03 | GitHub Actions `windows-2025` (`c7bbbe4`) | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40290363463; 421/421 and 425/425, 0 skipped |
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
