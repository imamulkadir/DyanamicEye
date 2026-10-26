# WORK_LOG.md — Archived agent history

Verbatim detail moved out of `docs/AGENT_PROGRESS.md` to keep startup context small (AGENTS.md). Newest entries first within each section. Append new archives at the top of the relevant section.

## Archived 2026-10-26 (summary lines P00 … P02-05, moved from RECENT VERIFIED CHANGES)

### Verified changes
- 2026-10-25 P02-05 pushed; CI run 40207131071 **success**: Release 342, Debug 343, **0 skipped**, including the fullscreen end-to-end and visual regression tests.
- 2026-10-25 P02-04 + P02-05 IMPLEMENTED_AWAITING_VALIDATION: fullscreen detection with the cloaked-window fix, idle budget (0 ms CPU), power-aware motion, notch visual regression (ADRs 0010/0011). Details are in WORK_LOG.
- 2026-10-25 P02-03 IMPLEMENTED_AWAITING_VALIDATION + notch redesign: state machine, UIA, keyboard/focus, notch design (ADR 0009 + amendment); CI 40204446036 success. Details are in WORK_LOG.
- 2026-10-25 P02-02 IMPLEMENTED_AWAITING_VALIDATION: display-change debounce, placement controller, click-through, DPI scope, original icon (ADR 0008 amendment). Details are in WORK_LOG.
- 2026-10-25 P02-01 pushed; CI run 40201946771 **success**, including the placement smoke test on the runner.
- 2026-10-25 P02-01 DONE: island window, Win32 monitor service, placement calculator, PerMonitorV2 manifest (ADR 0008). Details are in WORK_LOG.
- 2026-10-25 P01-05 pushed; CI run 40201274900 **success**. **G01 confirmed on CI.**
- 2026-10-25 P01-05 DONE: component error boundaries, notices, diagnostics preview (ADR 0007). Details are in WORK_LOG.
- 2026-10-25 P01-04 pushed; CI run 40200547110 **success** (Release and Debug steps, including the crash-injection smoke tests on the runner).
- 2026-10-25 P01-04 DONE: crash supervision, launcher, Safe Mode, Debug-only test hooks (ADR 0006). Details are in WORK_LOG.
- 2026-10-25 P01-03 DONE + CI fix: lifecycle coordinator, single instance with acknowledged pipe (ADR 0005); CI 40199779718 success.
- 2026-10-25 P01-02 DONE: redacting rolling log (ADR 0004). P01-01 DONE: versioned settings store (ADR 0003).
- 2026-10-25 P00-01..04 DONE: baseline, SDK pin, solution/build policy, Windows CI; G00 PASS.

## Archived 2026-10-25 (P02-04, P02-05)

### Verified changes
- 2026-10-25 **P02-05 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; owner commits). Decisions are in ADR 0011 and the ADR 0010 amendment.
  - **Core:** `Shell/MotionPolicy.cs` (`IMotionGate`, pure `MotionPolicy`: off/reduced/saver → no animation).
  - **Platform.Windows:**
    - `Power/PowerStatus.cs` (`GetSystemPowerStatus` saver flag)
    - fullscreen detector fix: `EVENT_OBJECT_UNCLOAKED` hook, one-shot 0.5 s follow-up after a foreground change, thread-safe `Refresh`, `ActiveHookCount`
  - **App:**
    - New `MotionGate.cs`.
    - New `NotchView.xaml(.cs)`: the notch visuals extracted from `IslandWindow` with `ApplyVisualState`.
    - `IslandWindow.xaml` is now hosting only; the code-behind uses `NotchView` and `IMotionGate`.
    - `FullscreenCoordinator` logs the hook count; `AppHost` registers `IMotionGate`.
  - **Tests:**
    - `UnitTests/Shell/MotionPolicyTests.cs` (7)
    - `IntegrationTests/NotchVisualTests.cs` (4 states) with `References/*.png` (our renderings, reviewed)
    - smoke `IdleBudgetSmokeTests.cs`
    - `FullscreenSmokeTests` sizes the helper before showing it and prints the app log tail on failure
    - The integration csproj gains `References` copy and a global `System.IO` using; redundant per-file usings were removed.
  - **CI/README:** tests run with `--max-parallel-test-modules 1`; the CI artifact includes `VisualDiffs`.
  - **Docs:** `TEST_MATRIX.md` has the G02 sign-off checklist (P1, P2, V1).
- 2026-10-25 P02-04 pushed; CI run 40205375218 **FAILED** (fullscreen smoke test: island did not hide). Root cause was cloaked windows, fixed above and not yet pushed.
- 2026-10-25 **P02-04 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; owner commits). Decisions are in ADR 0010.
  - **Core:**
    - `Display/Fullscreen.cs`: `FullscreenSource`, `FullscreenSnapshot`, `IFullscreenDetector`, pure `FullscreenPolicy`.
    - Settings `Behavior.FullscreenExceptions` (validated exe names, ≤ 50, never logged) with value equality.
  - **Platform.Windows:**
    - `Display/Win32FullscreenDetector.cs`: WinEvent hooks for foreground, minimize-end and foreground location change; covers-monitor and not-maximized-with-caption check; shell, cloaked and own process excluded; D3D/presentation notification state.
    - `Win32MonitorService.IdFor`/`BoundsOf`.
    - `IslandWindowInterop.IsForeground` and `SetBounds(..., topmost)`.
    - New interop.
  - **App:**
    - `FullscreenCoordinator.cs`: 300 ms debounce → policy → state machine; logs hidden true/false only.
    - `AppHost` registrations; `App.xaml.cs` starts it after the window. `AppLog.FullscreenHide`.
  - **Tests:**
    - `UnitTests/Display/FullscreenPolicyTests.cs` (policy ×9, validation ×8)
    - `IntegrationTests/Win32FullscreenDetectorTests.cs` (5, real helper windows)
    - smoke `FullscreenSmokeTests.cs` (end to end; skipped if Windows refuses foreground)
    - shared `tests/Shared/StaDispatcherThread.cs`, linked into both Windows test projects
  - **Debugging note:** the first full run failed 2 smoke tests. Integration helper windows were topmost and covered the screen while the smoke tests ran in parallel. Helpers are now non-topmost; temporary diagnostics were added and removed.
- 2026-10-25 Notch design + P02-03 pushed; CI run 40204446036 **success**.

### Test evidence
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P02-05 | Windows 11 Pro 25H2 x64, 1 monitor at 100 % | Reproduced the CI failure locally (Release suite) | FAIL → fixed | The app log showed hooks installed but no fullscreen detection. Cause: the new window was DWM-cloaked during the foreground event. Fix: uncloak hook + one-shot follow-up. Release smoke 3/3 afterwards. |
| 2026-10-25 | P02-05 | same, `CI=true` | `dotnet test --solution … --max-parallel-test-modules 1`, Release and Debug ×2; plus Release in parallel mode ×2 | PASS | Release 342/342 ×4, Debug 343/343 ×2, **0 skipped**, no stray processes |
| 2026-10-25 | P02-05 | same | Idle budget smoke: 10 s idle, collapsed | PASS | **0 ms CPU**, working set 94 MiB |
| 2026-10-25 | P02-05 | same | Visual references generated and reviewed (composite preview of the 4 states) | PASS | Notch outline, shoulders, sensor housing, expanded panel and green pin correct |
| — | G02 | real hardware | `TEST_MATRIX.md` G02 NOT_RUN rows | PENDING_WINDOWS | Owner |
| 2026-10-25 | P02-04 | Windows 11 Pro 25H2 x64, one monitor at 100 % | `dotnet test --solution` (first run) | FAIL → fixed | Release: the fullscreen smoke test did not hide; Debug: the corner click-through failed. Cause: integration helper windows were topmost and covered the screen in parallel. Fixed with `topmost: false`. |
| 2026-10-25 | P02-04 | same | `dotnet build` + `dotnet test --solution` for Release and Debug, twice each | PASS | 0 warnings. Release 330/330 ×2 (end-to-end fullscreen hide and return **passed**). Debug 330 passed + 1 **skipped** ×2: Windows refused foreground to the test window, reported as a skip. |
| 2026-10-25 | P02-04 | same | Integration `Win32FullscreenDetectorTests` on real windows | PASS | Covering window → fullscreen on the primary monitor with the exe name; half-size, maximized-with-caption and own-process windows → not fullscreen |
| — | P02-04 | real video, game, browser F11, presentation, 2 monitors | `docs/TEST_MATRIX.md` rows F1–F8 | PENDING_WINDOWS | Owner to run |

## Archived 2026-10-25 (P02-03, notch redesign, P02-02 evidence)

### Verified changes
- 2026-10-25 **Design change (owner request): the island is now a classic iPhone-12-Pro-style notch.** Uncommitted. See the ADR 0009 amendment.
  - `IslandSurface` is a custom-drawn `Decorator`: flat top flush with the screen edge, 6 DIP concave shoulders, rounded bottom corners, black; geometry-based hit testing.
  - `IslandWindow.xaml` adds the sensor housing (speaker slit, front-camera lens, sensor dot) in every state. Expanded content sits below it. The green status dot was removed.
  - `IslandLayout`: collapsed 236×32 and peek 264×38 (expanded 420×180 unchanged).
  - Defaults: `VerticalOffset` 0 and `Opacity` 1.0.
  - New `IntegrationTests/NotchGeometryTests.cs` (9).
  - `TEST_MATRIX.md` sizes and wording updated.
- 2026-10-25 **P02-03 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; owner commits). Decisions are in ADR 0009.
  - **Core:**
    - `Shell/IslandStates.cs`: `IslandState`, `IslandInput`, `IslandSnapshot`, the pure `IslandTransitions.Next`, `IslandLayout` sizes.
    - `Shell/IslandStateMachine.cs`: `IIslandStateMachine` with a 250 ms hover debounce, posted to the UI thread.
    - `Debouncer.Cancel()`.
  - **Platform.Windows:** `IslandWindowInterop.IsVisible` and `TryActivate` (focus return without force); `IsWindow`, `IsWindowVisible` and `SetForegroundWindow` interop.
  - **App:**
    - New `IslandSurface.cs`: the pill with a UIA peer exposing ExpandCollapse and Toggle, AutomationId `IslandSurface`.
    - `IslandWindow.xaml` rewritten: the window has the fixed expanded size; the animated pill holds the expanded placeholder panel and a pin toggle styled for the dark theme.
    - `IslandWindow.xaml.cs`:
      - wires pointer, click, keys (Enter/Space, Escape, Ctrl+P) and the pin into the state machine
      - animates size per state (motion preference / Windows animation setting)
      - hides the window in hidden states
      - records `WM_ACTIVATE` for focus return
      - aligns the pill per placement
      - places the window at the max size
    - `AppHost` registers `IIslandStateMachine`.
  - **Tests:**
    - `UnitTests/Shell/IslandStateMachineTests.cs`: 84-case exhaustive table, 9 scenarios, 4 hover/debounce tests with `ManualTimeProvider`.
    - Smoke: new `IslandInteractionSmokeTests` (UIA expand → pin → collapse; pill size per state within ±3 px; window size unchanged).
    - `IslandPlacementSmokeTests` updated for the fixed window (center hit; corner and area under the pill click-through).
    - Smoke csproj gains `UseWPF` (UIA client) and a `System.IO` using.
  - **Docs:** `TEST_MATRIX.md` rows H2–H9.
- 2026-10-25 P02-02 pushed; CI run 40202761834 **success**.

### Test evidence
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P02-03 notch | Windows 11 Pro 25H2 x64, one monitor at 100 % | Release and Debug build + `dotnet test --solution` | PASS | 0 warnings; Release 299/299, Debug 300/300 before the geometry tests; Integration 29/29 with `NotchGeometryTests` |
| 2026-10-25 | P02-03 notch | same, real desktop | Real app screenshot (2× zoom) collapsed, then UIA Expand screenshot | PASS | Window `L=750 T=0 420×180`. The notch is flush with the top edge, with shoulders, speaker slit, camera lens and sensor dot. Expanded, it grows down with the title, placeholder and pin. |
| 2026-10-25 | P02-03 | Windows 11 Pro 25H2 x64, one monitor at 100 % | `dotnet build` + `dotnet test --solution` for Release and Debug, `CI=true` | PASS | 0 warnings (an earlier attempt failed on IDE0005; fixed); Release 299/299, Debug 300/300; no stray processes |
| 2026-10-25 | P02-03 | same, real desktop | Real app driven through UI Automation from PowerShell: Expand → Toggle (pin); screenshots of collapsed, expanded and pinned | PASS | Window stays 420×180; collapsed capsule with dot; expanded panel with title and placeholder; pinned shows a green pin. The first screenshot showed WPF's default light toggle; restyled for the dark theme. UIA name "Dynamic Island, expanded and pinned". |
| — | P02-03 | real mouse, keyboard, Narrator, reduced motion | `docs/TEST_MATRIX.md` rows H2–H8 | PENDING_WINDOWS | Owner to run; agent avoids synthetic input on the user's desktop |
| 2026-10-25 | P02-02 | Windows 11 Pro 25H2 x64, one monitor 1920 px at 100 % | `dotnet build` + `dotnet test --solution` for Release and Debug | PASS | 0 warnings (an earlier attempt failed on CA1001; fixed); Release 200/200, Debug 201/201 |
| 2026-10-25 | P02-02 | same, real desktop | Smoke `Only_the_visible_pill_receives_clicks` | PASS | `WindowFromPoint`: pill center belongs to the island, top-left corner pixel does not (click-through) |
| 2026-10-25 | P02-02 | same | Icon preview rendered at 16/24/32/48/64/256 px; `ExtractIconExW` count ≥ 1 for both exes | PASS | Crisp original dark tile, light capsule, centered green dot. The first generator run produced a 142-byte file (PowerShell unrolled the byte arrays); fixed. |
| 2026-10-25 | P02-02 | same | Real app: `taskkill /IM DynamicIsland.exe` (no `/F`) | PASS | Clean exit, code 0; log shows `Island placed: primary monitor True, fallback False, scale 1, size 200x36 px` |
| — | P02-02 | multi-DPI, multi-monitor, taskbar, sleep/wake | `docs/TEST_MATRIX.md` rows D2–D5, M1–M6, T1–T2, S1–S2, I1, A1 | PENDING_WINDOWS | Owner to run on real hardware; a single 100 % monitor is visible to the agent |

## Archived 2026-10-25 (P02-02)

### Verified changes
- 2026-10-25 **P02-02 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; owner commits). See the ADR 0008 amendment.
  - **Core:**
    - `Display/Debouncer.cs`: TimeProvider timer that coalesces bursts.
    - `Display/IslandPlacementController.cs`: re-evaluates each pass, falls back while the preferred monitor is gone and restores it when it returns, reports `Changed`/`MonitorChanged`.
  - **Platform.Windows:**
    - `Display/DpiAwarenessScope.cs`: thread per-monitor-v2 scope for measuring.
    - `IslandWindowInterop.ReceivesClickAt` (`WindowFromPoint` + `GetAncestor`).
    - New interop: `SetThreadDpiAwarenessContext`, `WindowFromPoint`, `GetAncestor`.
  - **App:**
    - `IslandWindow` is now sealed and `IDisposable`. It uses the controller and a 250 ms debouncer over `WM_DISPLAYCHANGE`, the `SPI_SETWORKAREA` setting change, `SystemEvents.DisplaySettingsChanged` and resume from sleep. A DPI change re-places immediately, and `SystemEvents` are unsubscribed on close.
    - `AppLog.IslandPlaced` (no monitor id).
    - App and Launcher csproj gain `ApplicationIcon`.
  - **Assets:** `assets/DynamicIsland.ico` (original, 16–256 px), generated by `tools/New-AppIcon.ps1`. `THIRD_PARTY_NOTICES.md` notes it as original work.
  - **Docs:** new `docs/TEST_MATRIX.md` (G02 manual rows, G01 summary).
  - **Tests:**
    - `UnitTests/Display/DisplayChangeTests.cs`: debouncer ×4, controller ×4.
    - `TestSupport/ManualTimeProvider.cs`: deterministic timers.
    - Smoke `IslandPlacementSmokeTests`: placement now measured under the DPI scope; new click-through test (center hits, corner passes through); icon present in both exes. The smoke csproj gains `AllowUnsafeBlocks`.

## Archived 2026-10-25 (P02-01)

### Verified changes
- 2026-10-25 **P02-01 DONE** (uncommitted; owner commits). Decisions are in ADR 0008.
  - **Core:**
    - `Display/Display.cs`: `PixelRect`, `MonitorInfo`, `IMonitorService`, `PlacementRequest`/`PlacementResult`, and `IslandPlacementCalculator` (work area, offsets, clamping, preferred → primary → first fallback).
    - Settings gain `Appearance.HorizontalOffset` (0, ±4000) and `VerticalOffset` (8, 0..1000), validated without a schema bump.
  - **Platform.Windows:**
    - `Display/Win32MonitorService.cs`: `EnumDisplayMonitors` with an `UnmanagedCallersOnly` callback, `GetMonitorInfoW`, `GetDpiForMonitor`; ids are device interface paths.
    - `Display/IslandWindowInterop.cs`: tool window, `SetBounds`, `GetBounds`, `IsTopmost`.
    - `NativeMethods.cs`: new interop.
  - **App:**
    - New `IslandWindow.xaml(.cs)`: an original capsule. It re-places itself on DPI, display and work-area changes, coalesced.
    - New `AppSettingsSnapshot.cs`.
    - New `app.manifest`: PerMonitorV2, asInvoker.
    - `MainWindow.xaml(.cs)` **deleted**.
    - `AppHost` registers the snapshot, monitor service and window. `App.xaml.cs` passes loaded settings to the snapshot.
  - **Tests:**
    - `UnitTests/Display/IslandPlacementCalculatorTests.cs` (13)
    - `SettingsValidatorTests`: offsets clamped
    - `IntegrationTests/Win32MonitorServiceTests.cs` (2)
    - `WindowsSmokeTests/IslandPlacementSmokeTests.cs`: compares the real window rectangle to the calculator within ±2 px; checks tool window and topmost.
  - **Deviation from the plan:** no `MonitorsChanged` event on `IMonitorService` yet. The window's message hook drives re-placement; P02-02 decides whether the event is needed.

### Test evidence
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P02-01 | Windows 11 Pro 25H2 x64, single monitor 1920 px wide at 96 DPI (100 %) | `dotnet build` and `dotnet test --solution` for Release and Debug | PASS | 0 warnings; Release 189/189, Debug 190/190 |
| 2026-10-25 | P02-01 | same, real desktop | Launched the Release `DynamicIsland.exe`; read `GetWindowRect` as a per-monitor-aware process; screenshot of the top 120 px | PASS | Rect `L=860 T=8 W=200 H=36` (exactly top-center, 8 DIP gap); window DPI 96; screenshot shows a rounded dark capsule with a green dot above other windows; exit code 0 |

## Archived 2026-10-25 (P01-04/P01-05 evidence)

| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P01-05 | GitHub Actions `windows-2025`, owner push | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40201274900 (confirms G01) |
| 2026-10-25 | P01-05 | Windows 11 Pro 25H2 26200.9711 x64, SDK 10.0.401 | `dotnet build DynamicIsland.sln -c Release\|Debug --no-restore` | PASS | 0 warnings. An earlier attempt failed on IDE0005 and CA2201 in a test file; fixed. |
| 2026-10-25 | P01-05 | same | `dotnet test --project tests/DynamicIsland.UnitTests -c Release --no-build` 3 times with all 6 cores saturated | PASS | 151/151 each run |
| 2026-10-25 | P01-05 | same, `CI=true`, real desktop | `dotnet test --solution DynamicIsland.sln -c Release\|Debug --no-build`, twice each | PASS | Release 173/173, Debug 174/174; no stray processes |

## Archived 2026-10-25 (P01-05)

### Verified changes
- 2026-10-25 **P01-05 DONE** (uncommitted; owner commits). Decisions are in ADR 0007.
  - **Core:**
    - `Components/ComponentContracts.cs`: `IIsolatedComponent`, `IComponentContext`, `ComponentState`, `ComponentStatus`, `FailureKind`, `ComponentFailureException`, `ReasonCodes`, `DefaultFailureClassifier`, `RetryPolicy`.
    - `Components/ComponentBoundary.cs`: transient failures retry forever with capped jittered backoff; recoverable ones retry 3× then `Failed`; fatal ones request shutdown.
    - `Components/ComponentHost.cs`: skips optional components in Safe Mode; stops are registered with the lifecycle.
    - `Notifications/Notifications.cs`: `UserNotice` (validated keys only), `INotificationService`, `InMemoryNotificationService` (10-minute de-dup, 5 per minute, 20 active).
  - **Infrastructure:**
    - `Diagnostics/DiagnosticBundleBuilder.cs`: re-redacted preview (summary plus the last 500 log lines each); `WriteZipAsync` writes the approved preview plus a SHA-256 manifest and never overwrites.
    - `JsonCrashStateStore.ReadRecentCrashes`.
  - **App:** `AppHost` registers notices, the classifier, the component host and the diagnostics builder. `App.xaml.cs` logs status changes and notices and starts the component host after the window. `AppLog` adds 2 events.
  - **Tests:**
    - `UnitTests/Components/ComponentBoundaryTests.cs` (8)
    - `UnitTests/Components/FailureHandlingTests.cs`: retry, classifier, notices
    - `UnitTests/Diagnostics/DiagnosticBundleBuilderTests.cs` (5)

## Archived 2026-10-25 (P01-04)

### Verified changes
- 2026-10-25 **P01-04 DONE** (uncommitted; owner commits). Decisions are in ADR 0006.
  - **Core:**
    - `Lifecycle/CrashPolicy.cs`: 2 restarts per 5-minute window → Safe Mode once → stop.
    - `Lifecycle/AppSupervisor.cs`: `ICrashStateStore`, `IAppProcessRunner`, `IRecoveryNotifier`, `SupervisorStep`/`SupervisorAction`. Exit 0 finishes, exit 3 relaunches (at most 5 times), anything else counts as a crash.
    - `Lifecycle/LaunchArguments.cs`: allow-listed `--safe-mode`; other arguments are counted and ignored.
    - `Diagnostics/TestHooks.cs`: env-var hooks active only with `DYNAMICISLAND_TEST_HOOKS`, which is defined in Debug builds only (`Directory.Build.props`).
  - **Infrastructure:**
    - `Lifecycle/JsonCrashStateStore.cs` (`data/crash-history.json`; atomic; falls back to memory).
    - `AppDataPaths.Root` honours the test data-root hook.
    - `FileLoggerOptions.FilePrefix` is now configurable and validated (constant renamed to `DefaultFilePrefix`).
  - **Launcher:** real implementation replaces the stub; `WinExe`.
    - `Program.cs`, `ProcessAppRunner.cs`, `MessageBoxRecoveryNotifier.cs` (native `MessageBoxW`), `LauncherLog.cs`.
    - csproj references Core and Infrastructure, with `AllowUnsafeBlocks`.
  - **App:**
    - `App.xaml.cs` parses `LaunchArguments`, logs Safe Mode and ignored arguments, adds a "(Safe Mode)" title suffix, and has the Debug-only crash hook.
    - `AppHost.CreateBuilder(dataRoot, launchArguments)`.
    - `AppLog` adds `SafeModeActive` and `ArgumentsIgnored`.
  - **Tests:**
    - `UnitTests/Lifecycle/CrashRecoveryTests.cs`: policy, supervisor with a scripted runner, crash store, arguments.
    - `DependencyBoundaryTests`: Launcher may reference Core and Infrastructure.
    - Smoke: new `RealApp.cs` (shared helpers plus a non-parallel `RealApp` collection), `CrashRecoverySmokeTests.cs`, refactored `AppLaunchSmokeTests.cs`. The smoke csproj references the Launcher.
  - **CI:** `ci.yml` gained Debug build + Debug test steps.
  - **Docs:** progress compacted; history moved to `docs/WORK_LOG.md`.
  - **Bugs found by the new tests and fixed:** the crash store lost its in-memory history when the folder was unusable (it now merges); CA1711 and CA1822 analyzer findings.

### Test evidence
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P01-03 fix | GitHub Actions `windows-2025` | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40199779718 |
| 2026-10-25 | P01-04 | Windows 11 Pro 25H2 26200.9711 x64, SDK 10.0.401 | `dotnet build DynamicIsland.sln -c Release` and `-c Debug` | PASS | 0 warnings each. Earlier attempts failed on CA1822 and CA1711; fixed. |
| 2026-10-25 | P01-04 | same | `dotnet test --solution DynamicIsland.sln -c Release\|Debug --no-build` (first run) | FAIL → fixed | 4 failures: 3 were a wrong test expectation (a second `RecordCrash` adds a crash), 1 was a real bug (history lost when the folder was unusable) |
| 2026-10-25 | P01-04 | same, `CI=true`, real desktop | Release and Debug, each run twice | PASS | Release 146/146, Debug 147/147 both times. No stray processes and no leftover temp data root. The real profile holds only the Release smoke logs (no crash history). |
| 2026-10-25 | P01-04 | same, Debug end-to-end | `CrashRecoverySmokeTests.Crash_loop_…`: launcher + app with `DYNAMICISLAND_TEST_CRASH=ui` in a temp data root | PASS | Launcher log shows 4 runs: Relaunch, Relaunch, RelaunchInSafeMode, then Stop (safe mode True). Launcher exit code 1. App log has one `SafeModeActive`. |
| 2026-10-25 | P01-04 | same, Release | `Release_build_ignores_the_test_crash_switch` | PASS | With the crash and data-root variables set, Release starts normally, exits 0 and creates no temp data root |

## Archived 2026-10-25 (P00-01 … P01-03)

### Verified changes
- 2026-10-25 P01-03 CI fix (uncommitted; owner commits). CI run 40198912460 on `c0c237f` failed 2 tests (119/121):
  - (a) `Components_beyond_the_total_budget_are_skipped` was a test flaw: real Windows timers fire up to about 15 ms early. Now it uses a deterministic clock; `TestSupport/FixedTimeProvider` also drives `GetTimestamp`.
  - (b) `Unknown_commands_are_ignored...` exposed a real protocol weakness: activation was fire-and-forget, so a request could be lost while the sender reported success. `SingleInstanceGuard` now uses request/reply (`activate\n` → `ok\n`/`no\n`). The callback runs before the reply, and the server drains until the client closes. Tests were rewritten to assert deterministically, plus a new unterminated-command test. ADR 0005 updated.
- 2026-10-25 P01-03 DONE (uncommitted; owner commits). Decisions are in ADR 0005.
  - **Core `Lifecycle/`:**
    - `ExitReason.cs` (+ `ExitCodes`)
    - `ISessionStateMarker.cs`
    - `LifecycleCoordinator.cs`: one-shot request, stopping token, reverse-order stop with 2 s per step and a 5 s total budget, failures reported by type.
  - **Infrastructure:** `Lifecycle/FileSessionStateMarker.cs` (`data/session.running`).
  - **Platform.Windows:**
    - `Instance/SingleInstanceGuard.cs`: `Local\` + SID mutex; current-user-ACL pipe with `FirstPipeInstance`; client uses `CurrentUserOnly`; exact `activate` command, 64-byte and 2 s limits.
    - `NativeMethods.cs` (`AllowSetForegroundWindow`)
    - csproj: `AllowUnsafeBlocks` for LibraryImport; `InternalsVisibleTo` IntegrationTests.
  - **App:**
    - `App.xaml.cs` rewritten. Startup order: instance → host → marker → settings → window → listener. Handles `SessionEnding` and unhandled exceptions; Fatal is a controlled exit.
    - `AppLog.cs`: 9 more events.
    - `AppHost.cs` registers the coordinator and the marker.
  - **Build:** `Directory.Build.props` now also suppresses CS1573 (partial param docs).
  - **Tests:** `UnitTests/Lifecycle/LifecycleCoordinatorTests.cs`, `UnitTests/Lifecycle/FileSessionStateMarkerTests.cs`, `IntegrationTests/SingleInstanceGuardTests.cs`, `WindowsSmokeTests/AppLaunchSmokeTests.cs` (adds the second-launch test).
  - **Not covered yet:** the UI-thread fatal path has no injected-crash test; it is planned in P01-04.
- 2026-10-25 P01-02 pushed by owner (`9a8f822`); CI run 40198235944 **success**.
- 2026-10-25 P01-02 DONE (uncommitted; owner commits). Decisions are in ADR 0004.
  - **Core:** `Diagnostics/LogRedactor.cs`. Redacts profile path, user/machine/domain names (whole words), GitHub/bearer tokens, secret assignments and emails; fails closed on regex timeout.
  - **Infrastructure:**
    - `Logging/FileLoggerOptions.cs`
    - `Logging/FileLoggerProvider.cs`
    - `Logging/RollingLogWriter.cs`: JSON lines, UTC daily + 5 MiB roll, 14-day retention, 50 MiB cap, bounded queue that drops the oldest entries with a count, disables itself on I/O errors.
    - `AppDataPaths` gained `Logs` and folder-name constants.
    - csproj: added `Microsoft.Extensions.Logging.Abstractions` and `InternalsVisibleTo` UnitTests.
  - **App:**
    - `AppHost.CreateBuilder(string? dataRoot)` registers the redactor and file logger (the only provider), sets the level to Information with Microsoft/System raised to Warning, and roots settings in the data root.
    - New `AppLog.cs` (`[LoggerMessage]` started/exiting events).
    - `App.xaml.cs` logs startup (version, OS, architecture) before the window, and logs exit.
  - **Build:** `Directory.Packages.props` pins Logging.Abstractions 10.0.13. `.editorconfig` turns off CA1848/CA1873 in tests only. Lock files updated.
  - **Tests:** `UnitTests/Diagnostics/LogRedactorTests.cs`, `UnitTests/Logging/FileLoggerProviderTests.cs`, `IntegrationTests/AppHostTests.cs` (rewritten to use a temp data root). `TestSupport/FixedTimeProvider` is now settable.
  - **Bug found by tests and fixed:** `FileInfo.Length` read after `Delete()` threw and disabled the sink during the size-cap pass.
  - **Docs:** `THIRD_PARTY_NOTICES.md` usage updated; ADR 0004.
- 2026-10-25 P01-01 pushed by owner (`e4a32ba`); CI run 40197494204 **success**.
- 2026-10-25 P01-01 DONE (uncommitted; owner commits). Decisions are in ADR 0003.
  - **Core `Settings/`:** `AppSettings.cs` (records and enums), `WidgetIds.cs`, `SettingsValidator.cs`, `ISettingsStore.cs` (load result, outcomes, save result).
  - **Infrastructure:**
    - `AppDataPaths.cs`
    - `Settings/JsonSettingsStore.cs`: atomic replace plus `.bak`, corrupt-file quarantine (newest 5 kept) → backup → defaults, read-only on a newer schema or a failed migration, versioned backup before migrating, 1 MiB cap, BOM/comments/trailing commas tolerated, serialized access.
    - `Settings/SettingsJson.cs`: reflection-based STJ, because the source generator dropped record defaults; a test caught this.
    - `Settings/ISettingsMigration.cs`, `Settings/SettingsStoreOptions.cs`.
  - **App:** `AppHost` registers `TimeProvider.System`, `SettingsStoreOptions(AppDataPaths.Data)` and `ISettingsStore`. Nothing loads settings at startup yet; that is P01-03 lifecycle work.
  - **Tests:** `UnitTests/Settings/SettingsValidatorTests.cs`, `UnitTests/Settings/JsonSettingsStoreTests.cs`, `Settings/Fixtures/settings-v0.json`, `TestSupport/TempDirectory.cs`, `TestSupport/FixedTimeProvider.cs`. The UnitTests csproj copies the fixtures to output.
- 2026-10-25 P00-04 DONE: the owner pushed commit `963b01c` to `master`. CI run 40196393380 succeeded with every step green, which satisfies **G00**.
- 2026-10-25 P00-04 implementation details: Added `.github/workflows/ci.yml` and removed `.github/workflows/.gitkeep`.
  - **Runner and triggers:** `windows-2025`; push to `master`, PRs, `workflow_dispatch`.
  - **Hardening:** `permissions: contents: read`, concurrency group with cancel-in-progress, 20-minute timeout, checkout with `persist-credentials: false`.
  - **Pinned actions** (SHAs resolved from the GitHub API on 2026-10-25; inputs verified against each `action.yml`):
    - checkout v7.0.1 `3d3c42e5aac5ba805825da76410c181273ba90b1`
    - setup-dotnet v6.0.0 `a98b56852c35b8e3190ac28c8c2271da59106c68` (uses `global.json`, NuGet cache keyed on lock files)
    - upload-artifact v7.0.2 `cf430e030ddbb5b0abf93d22962f4752f3646cd9` (TRX diagnostics only, 14-day retention)
  - **Steps:** locked restore → Release build → `dotnet test --solution ... --report-xunit-trx`.
  - No release artifacts and no secrets used.
  - **Not done:** the YAML is reviewed manually only; no actionlint or YAML parser is available, and none was installed.
- 2026-10-25 P00-03 DONE (uncommitted; owner commits):
  - **Solution:** `DynamicIsland.sln` with 7 `src` projects and 3 `tests` projects. TFMs and decisions are in ADR 0002.
  - **Build policy:** `.gitattributes`, `.editorconfig`, `Directory.Build.props` (warnings as errors, latest-recommended analyzers, deterministic, lock files), `Directory.Packages.props` (central pinning: Hosting 10.0.13, xunit.v3 4.0.2), `nuget.config` (nuget.org only plus source mapping), `tests/Directory.Build.props`. `global.json` gained `test.runner = Microsoft.Testing.Platform`.
  - **App:** `AppHost.CreateBuilder()` (Generic Host, `DisableDefaults`, ValidateOnBuild/ValidateScopes). `App.xaml.cs` starts and stops the host. `MainWindow` is a labelled temporary dev window until P02. App assembly name is `DynamicIsland` (exe `DynamicIsland.exe`).
  - **Stubs:** `Launcher` and `Updater` print "not implemented" and exit 1. Core, Widgets, Platform.Windows and Infrastructure are empty projects.
  - **Tests:**
    - `UnitTests/Architecture/DependencyBoundaryTests.cs` + `RepositoryPaths.cs`
    - `IntegrationTests/AppHostTests.cs`
    - `WindowsSmokeTests/AppLaunchSmokeTests.cs`: launches the exe, waits for the main window, closes it, expects exit 0.
  - **Docs:** `THIRD_PARTY_NOTICES.md` (Microsoft.Extensions.* MIT; xunit.v3 Apache-2.0 test-only), ADR 0002. Removed `src/.gitkeep` and `tests/.gitkeep`.
- 2026-10-25 P00-02 DONE (uncommitted; owner commits): added `global.json` (SDK 10.0.401, `rollForward: latestFeature`, no prerelease); ADR 0001 supported Windows versions from official `dotnet/core` supported-os.md (updated 2026-09-28) + support policy (.NET 10 LTS EOL 2028-11-14); ADR index updated.
- 2026-10-25 P00-01 DONE (uncommitted; repo init deferred to owner): added `LICENSE` (MIT, "Dynamic Island for Windows contributors"), `THIRD_PARTY_NOTICES.md` (template, no components), `CONTRIBUTING.md`, `docs/ADR/README.md` (template + index), `.gitignore` (.NET/VS/test/packaging/runtime data); rewrote `README.md` as project README (kept agent instruction); created §3 skeleton dirs `src/`, `tests/`, `packaging/portable/`, `packaging/installer/`, `.github/workflows/` with `.gitkeep`. No product code. §3 docs `DESIGN_SYSTEM.md`, `SECURITY_PRIVACY.md`, `TEST_MATRIX.md`, `RELEASE_PROCESS.md` not created yet (owned by later tasks).
- 2026-10-25: Created implementation plan and handoff template.

### Test evidence
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P00-01 | Windows 11 Pro 10.0.26200 | Directory listing of repo root, `docs/`, `docs/ADR/` | PASS | All baseline files present; no build artifacts |
| 2026-10-25 | P00-02 | Windows 11 Pro 25H2 26200.9711 x64 | `dotnet --info`; `dotnet --list-runtimes`; `Get-ComputerInfo`; registry `CurrentVersion` | PASS | SDK 10.0.401, WindowsDesktop 10.0.12 present |
| 2026-10-25 | P00-02 | same | `dotnet --version` in repo root with `global.json` | PASS | Output `10.0.401` |
| 2026-10-25 | P00-03 | Windows 11 Pro 25H2 26200.9711 x64, SDK 10.0.401 | `dotnet restore DynamicIsland.sln --locked-mode` | PASS | 10 lock files present |
| 2026-10-25 | P00-03 | same | `dotnet build DynamicIsland.sln -c Release --no-restore` | PASS | 0 warnings, 0 errors. The first attempt failed on IDE0005 (unused using), which shows the analyzer gate works; fixed. |
| 2026-10-25 | P00-03 | same | `dotnet test --solution DynamicIsland.sln -c Release` | PASS | 14/14 (Unit 10, Integration 3, WindowsSmoke 1) |
| 2026-10-25 | P00-03 | same | Mutation: temporarily added Widgets→Infrastructure reference, then ran UnitTests; reverted afterwards | PASS (expected fail observed) | `Project_references_follow_dependency_direction(Widgets)` failed. File restored byte-identical. |
| 2026-10-25 | P00-03 | same, interactive desktop | WindowsSmoke: real `DynamicIsland.exe` launch → window → `CloseMainWindow` → exit 0 | PASS | No leftover `DynamicIsland` process |
| 2026-10-25 | P00-04 | same, `CI=true` | Exact CI sequence locally: `dotnet restore --locked-mode`; `dotnet build -c Release --no-restore`; `dotnet test --solution DynamicIsland.sln -c Release --no-build --results-directory TestResults --report-xunit-trx` | PASS | 0 warnings; 14/14 tests; 3 TRX files produced (local `TestResults` then deleted) |
| 2026-10-25 13:10 UTC | P00-04 / G00 | GitHub Actions `windows-2025` (hosted), commit `963b01c`, `push` to `master` | CI workflow: checkout → setup-dotnet (global.json) → `restore --locked-mode` → Release build → `dotnet test --solution` → upload TRX | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40196393380. All steps `success`, verified through the public GitHub API. Per-test counts are only visible in the auth-gated log/artifact and were not read by the agent; the Test step passing includes the WindowsSmoke window test. |

| 2026-10-25 | P01-01 | Windows 11 Pro 25H2 26200.9711 x64, SDK 10.0.401 | `dotnet build DynamicIsland.sln -c Release` | PASS | 0 warnings, 0 errors |
| 2026-10-25 | P01-01 | same | `dotnet test --project tests/DynamicIsland.UnitTests -c Release` (first run) | FAIL → fixed | 2 failures showed that source-generated STJ drops init-property defaults. Switched to reflection-based STJ (ADR 0003 §8). |
| 2026-10-25 | P01-01 | same | `dotnet restore --locked-mode`; `dotnet test --solution DynamicIsland.sln -c Release --no-build` | PASS | 59/59: Unit 55 (settings 45 + architecture 10), Integration 3, Smoke 1. No files left in `%LOCALAPPDATA%\DynamicIsland` or temp. |
| 2026-10-25 | P01-01 | GitHub Actions `windows-2025`, commit `e4a32ba` | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40197494204 |
| 2026-10-25 | P01-02 | Windows 11 Pro 25H2 26200.9711 x64, SDK 10.0.401 | `dotnet restore`; `dotnet build DynamicIsland.sln -c Release --no-restore` | PASS | 0 warnings. Earlier attempts failed on CS1573, CA1873 and missing `System.IO` in WPF projects; all fixed. |
| 2026-10-25 | P01-02 | same | `dotnet test --solution DynamicIsland.sln -c Release --no-build` | FAIL → fixed | Size-cap test exposed the FileInfo.Length-after-Delete bug |
| 2026-10-25 | P01-02 | same | `dotnet test --solution DynamicIsland.sln -c Release --no-build`, 4 consecutive runs | PASS | 92/92 each run, no flakiness |
| 2026-10-25 | P01-02 | same, real desktop | The smoke-test launch of `DynamicIsland.exe` wrote `%LOCALAPPDATA%\DynamicIsland\logs\dynamicisland-20261025.log`; contents inspected | PASS | JSON lines with AppStarted/AppExiting; checked that user name and machine name are absent |
| 2026-10-25 | P01-02 | GitHub Actions `windows-2025`, commit `9a8f822` | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40198235944 |
| 2026-10-25 | P01-03 | Windows 11 Pro 25H2 26200.9711 x64, SDK 10.0.401 | `dotnet build DynamicIsland.sln -c Release --no-restore` | PASS | 0 warnings, after suppressing CS1573 repo-wide |
| 2026-10-25 | P01-03 | same | `dotnet test --solution DynamicIsland.sln -c Release --no-build`, 4 consecutive runs | PASS | 121/121 each run (includes the two-instance smoke test) |
| 2026-10-25 | P01-03 | same, real desktop | Real `DynamicIsland.exe`: corrupt `settings.json` and launch, then kill the process, then relaunch and close | PASS | Log: `ResetAfterCorruption`; marker kept after kill; next start logs `PreviousSessionUnclean`; clean close removes the marker, exit code 0. The test-created quarantine file was deleted afterwards. |
| 2026-10-25 | P01-03 | GitHub Actions `windows-2025`, commit `c0c237f` | CI workflow | **FAIL** | https://github.com/imamulkadir/DyanamicEye/actions/runs/40198912460: budget-skip test (timer granularity) and unknown-command activation test (lost fire-and-forget message). Passed locally, including with `CI=true`. |
| 2026-10-25 | P01-03 fix | Windows 11 Pro 25H2, 6 CPUs | `dotnet test --solution DynamicIsland.sln -c Release --no-build` 3 times; then Integration and Unit projects 3 times each with all 6 cores saturated by busy-loop jobs | PASS | 123/123 each run; 18/18 and 103/103 under load |
