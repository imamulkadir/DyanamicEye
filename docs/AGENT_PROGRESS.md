# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01 is done (G01 PASS, confirmed on CI run 40201274900). P02-01 is done; P02-02 is next.
- **Active task:** P02-02: per-monitor DPI, display-change reactions, hit-test geometry, high-DPI icons.
- **Active task status:** NOT_STARTED. P02-01 is DONE locally but uncommitted, awaiting owner review and push. Its multi-monitor/DPI aspects are `PENDING_WINDOWS` until G02.
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
  - `dotnet build DynamicIsland.sln -c Release --no-restore` and `dotnet test --solution DynamicIsland.sln -c Release --no-build`
  - the same two with `-c Debug`. Debug compiles the test hooks and runs the crash-injection smoke tests.

  The smoke tests start the real app and launcher on the desktop, and they assume no other Dynamic Island instance is running.
- **Last updated:** 2026-10-25 (P02-01 DONE; G01 confirmed on CI)

## NEXT ACTION (ONE task only)
**P02-02 — Per-monitor DPI awareness, display-change reactions, resolution/taskbar changes, correct no-click-through geometry, high-DPI icons.** (guide §5 P02-02, §4.1; ADR 0008)
1. **Display-change handling (keep the logic testable):** extract the re-placement trigger from `IslandWindow` into a small `DisplayChangeMonitor`.
   - Triggers: `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `WM_SETTINGCHANGE(SPI_SETWORKAREA)`, plus `SystemEvents.DisplaySettingsChanged` and `PowerModeChanged(Resume)` for sleep/wake.
   - Coalesce a burst of changes into one pass (unit-test the debouncer with a fake clock).
   - If the preferred monitor disappears, use the fallback placement; when it returns, restore it (test via the calculator plus a fake `IMonitorService`).
2. **DPI correctness:** confirm WPF honours PerMonitorV2. Moving between monitors with different DPI must keep the pill crisp and at the calculated physical size. Handle `WM_DPICHANGED` by re-placing after WPF's own resize; current code queues the reposition. Add a `MonitorsChanged`-style event to `IMonitorService` only if tests need it.
3. **Hit-test geometry:** clicks outside the visible pill, including the transparent rounded corners, must pass through to the windows below. The layered window's per-pixel alpha should already do this; verify with a smoke test that sends a click at a corner pixel to a helper window underneath. Document behaviour for future expanded states.
4. **High-DPI icons:** add an original multi-resolution app icon (`.ico` with 16/20/24/32/40/48/64/256 sizes, created in-repo from a simple vector shape). Reference it as the WPF `ApplicationIcon` and the launcher icon. List its origin in `THIRD_PARTY_NOTICES.md` as original work.
5. **Tests:** unit (debouncer, restore-after-reconnect logic); smoke (corner click-through, icon resource present). **Manual matrix for the owner** (record in TEST EVIDENCE as `PENDING_WINDOWS` until done): 100/125/150/200 % scaling, 2 monitors with mixed DPI, unplug/replug, rotate, taskbar moved to top/left, sleep/wake. Write exact steps in `docs/TEST_MATRIX.md` (guide §3 file).
6. **Verify:** Release + Debug builds with 0 warnings and all tests green; CI green after the owner pushes. Do not commit.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | DONE | **G01 PASS** 2026-10-25, CI run 40201274900 (Release + Debug). See the G01 evidence below. |
| P02 — Island shell | P02-01…P02-05 | IN_PROGRESS (P02-01 DONE) | G02 pending (needs a real multi-DPI/multi-monitor check) |
| P03 — Tray/settings/startup | P03-01…P03-05 | NOT_STARTED | G03 pending |
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
- 2026-10-25 P01-05 pushed; CI run 40201274900 **success**. **G01 confirmed on CI.**
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
- 2026-10-25 P01-04 pushed; CI run 40200547110 **success** (Release and Debug steps, including the crash-injection smoke tests on the runner).
- 2026-10-25 P01-04 DONE: crash supervision, launcher, Safe Mode, Debug-only test hooks (ADR 0006). Details are in WORK_LOG.
- 2026-10-25 P01-03 DONE + CI fix: lifecycle coordinator, single instance with acknowledged pipe (ADR 0005); CI 40199779718 success.
- 2026-10-25 P01-02 DONE: redacting rolling log (ADR 0004). P01-01 DONE: versioned settings store (ADR 0003).
- 2026-10-25 P00-01..04 DONE: baseline, SDK pin, solution/build policy, Windows CI; G00 PASS.

## TEST EVIDENCE (current task; older rows in `docs/WORK_LOG.md`)
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P01-05 | GitHub Actions `windows-2025`, owner push | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40201274900 (confirms G01) |
| 2026-10-25 | P02-01 | Windows 11 Pro 25H2 x64, single monitor 1920 px wide at 96 DPI (100 %) | `dotnet build` and `dotnet test --solution` for Release and Debug | PASS | 0 warnings; Release 189/189, Debug 190/190 |
| 2026-10-25 | P02-01 | same, real desktop | Launched the Release `DynamicIsland.exe`; read `GetWindowRect` as a per-monitor-aware process; screenshot of the top 120 px | PASS | Rect `L=860 T=8 W=200 H=36` (exactly top-center, 8 DIP gap); window DPI 96; screenshot shows a rounded dark capsule with a green dot above other windows; exit code 0 |
| — | P02-01 | multi-monitor, mixed DPI, 125–200 % | Placement on a secondary or high-DPI monitor, fallback when unplugged | PENDING_WINDOWS | Single 100 % monitor on the dev host; covered by unit tests; G02 matrix in P02-02 || 2026-10-25 | P01-04 | GitHub Actions `windows-2025`, owner push | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40200547110 |
| 2026-10-25 | P01-05 | Windows 11 Pro 25H2 26200.9711 x64, SDK 10.0.401 | `dotnet build DynamicIsland.sln -c Release\|Debug --no-restore` | PASS | 0 warnings. An earlier attempt failed on IDE0005 and CA2201 in a test file; fixed. |
| 2026-10-25 | P01-05 | same | `dotnet test --project tests/DynamicIsland.UnitTests -c Release --no-build` 3 times with all 6 cores saturated | PASS | 151/151 each run |
| 2026-10-25 | P01-05 | same, `CI=true`, real desktop | `dotnet test --solution DynamicIsland.sln -c Release\|Debug --no-build`, twice each | PASS | Release 173/173, Debug 174/174; no stray processes |

## BLOCKERS / RISKS
- Enterprise policy may block an unsigned per-user executable; never bypass it.
- Self-update and rollback must handle a read-only portable folder and DB schema compatibility (P06).
- The dev host is one security patch behind (runtime 10.0.12 / SDK 10.0.401 vs 10.0.13 / 10.0.402). The owner should update before any release build.
- Packaging (P06) must ship `DynamicIsland.Launcher.exe` next to `DynamicIsland.exe`, and shortcuts and startup target the launcher (ADR 0006).
- G02 requires multi-monitor, DPI 100–200 %, unplug/rotate and sleep/wake checks on real hardware. The dev host has one monitor visible to the agent, so the owner may need to run part of that matrix.

## ARCHITECTURE DECISIONS / ADRS
- Frozen D01–D15: see guide §0. No deviations approved.
- 0001 supported Windows versions · 0002 solution/build/test platform · 0003 settings persistence (consent defaults `NotAsked`; schema-bump rule for new enum values) · 0004 local logging and redaction · 0005 lifecycle, exit codes 0/1/3, single instance with acknowledged pipe · 0006 crash supervision, Safe Mode, Debug-only test hooks, launcher as the entry point · 0007 component error boundaries, notices (keys only), user-reviewed diagnostic export · 0008 island window (tool window + PerMonitorV2), monitor identity (device interface path, never logged), placement calculator.
- Pending: per-user installer technology and updater trust model (P06).

## HANDOFF NOTES
- Read `AGENTS.md` first; guide §5 has the task definitions. Current next task: **P02-02**.
- Widgets and providers (P04) must be `IIsolatedComponent`s registered as optional (ADR 0007). They must honor Safe Mode (ADR 0006) and never log free text (ADR 0004).
- If code is present that this file does not describe, inspect git and tests and reconcile this file before editing.
