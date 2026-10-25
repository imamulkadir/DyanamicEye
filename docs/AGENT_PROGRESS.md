# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01 is done (G01 PASS, confirmed on CI run 40201274900). P02-01 is done; P02-02 is implemented and awaiting the owner's manual DPI/multi-monitor matrix; P02-03 is next.
- **Active task:** P02-03: island state machine, hover/click/escape/pin, keyboard focus, animations.
- **Active task status:** NOT_STARTED. P02-02 is IMPLEMENTED_AWAITING_VALIDATION: automated tests pass and it is uncommitted. The owner runs `docs/TEST_MATRIX.md` rows D2–D5, M1–M6, T1–T2, S1–S2, I1 and A1. P02-01 was pushed and CI 40201946771 is green.
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
- **Last updated:** 2026-10-25 (P02-02 implemented; manual matrix pending)

## NEXT ACTION (ONE task only)
**P02-03 — Deterministic island state machine: hover debounce → peek, click → expand, collapse, pin, keyboard focus and Escape, transition animations; no invisible interaction blockers.** (guide §5 P02-03, §4.1, D07, D10)
1. **Core `Shell/IslandStateMachine` (pure; guide §3 `IIslandStateMachine`):**
   - States: `Hidden`, `Collapsed`, `Peek`, `Expanded`, `Pinned`, `SuspendedFullscreen`, `Paused`.
   - Inputs: `PointerEnter` / `PointerLeave` (with a hover debounce of about 250 ms through `TimeProvider`/`Debouncer`), `Click`, `Escape`, `TogglePin`, `FullscreenStarted` / `FullscreenEnded`, `Pause` / `Resume`, `Show` / `Hide`.
   - Rules (§4.1, D07):
     - Collapsed: hover leads to Peek after the debounce; a click leads to Expanded.
     - Pointer exit returns to Collapsed unless pinned.
     - Escape returns to Collapsed and unpins.
     - Pin keeps it expanded, except when fullscreen or pause overrides.
     - Fullscreen hides the island and **restores the previous visible state** when it ends.
     - Pause hides it while the tray remains, and Resume restores.
   - Deterministic: given the same inputs and clock you get the same states. Emit `StateChanged`.
   - **Exhaustive transition table test** covering every state × input pair.
2. **Sizes per state** (DIPs; tune visually):
   - Collapsed: 200×36
   - Peek: 280×44
   - Expanded and Pinned: 420×180
   - Hidden, SuspendedFullscreen and Paused: window hidden (not merely transparent), so it never blocks clicks.

   The window resizes to each state's size and re-places through `IslandPlacementController`. The request size comes from the state, and placement stays top-anchored.
3. **App wiring:**
   - `IslandWindow` forwards mouse enter/leave/click and keys to the state machine.
   - In Expanded it takes focus on click (so keyboard works) and returns focus without force on collapse (§4.1).
   - Expanded content is a placeholder panel for the widgets that arrive in P04.
   - Accessibility: automation names per state; Tab moves into the panel; Escape collapses.
4. **Motion (D08, D10):** animate size and opacity briefly, at most about 200 ms. Respect `Appearance.Motion` (`Off`/`Reduced` → none or a short fade) and the Windows "animation effects" setting (`SystemParameters.ClientAreaAnimation`). Animations must not keep running while idle.
5. **Tests:**
   - Unit: transition table and debounce with `ManualTimeProvider`.
   - Smoke (deterministic, no real mouse moves):
     - drive states through a test-only hook or UI Automation `Invoke` / `ExpandCollapse` on the island
     - assert the window rectangle matches each state's size, within ±2 px of the calculator
     - after Escape, the collapsed rectangle is restored
     - hidden states are not visible (`IsWindowVisible` is false)
6. **Verify:** Release + Debug builds with 0 warnings and all tests green; CI green after the owner pushes. Add matrix rows to `docs/TEST_MATRIX.md`: hover peek, click expand, Escape, pin, keyboard-only use, Narrator names, reduced motion. Do not commit.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | DONE | **G01 PASS** 2026-10-25, CI run 40201274900 (Release + Debug). See the G01 evidence below. |
| P02 — Island shell | P02-01…P02-05 | IN_PROGRESS (01 DONE; 02 IMPLEMENTED_AWAITING_VALIDATION) | G02 pending (needs a real multi-DPI/multi-monitor check) |
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
- 2026-10-25 P02-01 pushed; CI run 40201946771 **success**, including the placement smoke test on the runner.
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
- 2026-10-25 P01-05 DONE: component error boundaries, notices, diagnostics preview (ADR 0007). Details are in WORK_LOG.
- 2026-10-25 P01-04 pushed; CI run 40200547110 **success** (Release and Debug steps, including the crash-injection smoke tests on the runner).
- 2026-10-25 P01-04 DONE: crash supervision, launcher, Safe Mode, Debug-only test hooks (ADR 0006). Details are in WORK_LOG.
- 2026-10-25 P01-03 DONE + CI fix: lifecycle coordinator, single instance with acknowledged pipe (ADR 0005); CI 40199779718 success.
- 2026-10-25 P01-02 DONE: redacting rolling log (ADR 0004). P01-01 DONE: versioned settings store (ADR 0003).
- 2026-10-25 P00-01..04 DONE: baseline, SDK pin, solution/build policy, Windows CI; G00 PASS.

## TEST EVIDENCE (current task; older rows in `docs/WORK_LOG.md`)
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P02-01 | GitHub Actions `windows-2025` | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40201946771 |
| 2026-10-25 | P02-02 | Windows 11 Pro 25H2 x64, one monitor 1920 px at 100 % | `dotnet build` + `dotnet test --solution` for Release and Debug | PASS | 0 warnings (an earlier attempt failed on CA1001; fixed); Release 200/200, Debug 201/201 |
| 2026-10-25 | P02-02 | same, real desktop | Smoke `Only_the_visible_pill_receives_clicks` | PASS | `WindowFromPoint`: pill center belongs to the island, top-left corner pixel does not (click-through) |
| 2026-10-25 | P02-02 | same | Icon preview rendered at 16/24/32/48/64/256 px; `ExtractIconExW` count ≥ 1 for both exes | PASS | Crisp original dark tile, light capsule, centered green dot. The first generator run produced a 142-byte file (PowerShell unrolled the byte arrays); fixed. |
| 2026-10-25 | P02-02 | same | Real app: `taskkill /IM DynamicIsland.exe` (no `/F`) | PASS | Clean exit, code 0; log shows `Island placed: primary monitor True, fallback False, scale 1, size 200x36 px` |
| — | P02-02 | multi-DPI, multi-monitor, taskbar, sleep/wake | `docs/TEST_MATRIX.md` rows D2–D5, M1–M6, T1–T2, S1–S2, I1, A1 | PENDING_WINDOWS | Owner to run on real hardware; a single 100 % monitor is visible to the agent |
| 2026-10-25 | P02-01 | Windows 11 Pro 25H2 x64, single monitor 1920 px wide at 96 DPI (100 %) | `dotnet build` and `dotnet test --solution` for Release and Debug | PASS | 0 warnings; Release 189/189, Debug 190/190 |
| 2026-10-25 | P02-01 | same, real desktop | Launched the Release `DynamicIsland.exe`; read `GetWindowRect` as a per-monitor-aware process; screenshot of the top 120 px | PASS | Rect `L=860 T=8 W=200 H=36` (exactly top-center, 8 DIP gap); window DPI 96; screenshot shows a rounded dark capsule with a green dot above other windows; exit code 0 |
| — | P02-01 | multi-monitor, mixed DPI, 125–200 % | Placement on a secondary or high-DPI monitor, fallback when unplugged | PENDING_WINDOWS | Single 100 % monitor on the dev host; covered by unit tests; G02 matrix in P02-02 || 2026-10-25 | P01-04 | GitHub Actions `windows-2025`, owner push | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40200547110 |

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
