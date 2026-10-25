# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01 is done (G01 PASS, confirmed on CI run 40201274900). P02-01 is done; P02-02 and P02-03 are implemented and awaiting the owner's manual matrix; P02-04 is next.
- **Active task:** P02-04: fullscreen detection, game-safe hide/restore, exceptions.
- **Active task status:** NOT_STARTED. P02-03 is IMPLEMENTED_AWAITING_VALIDATION: automated tests pass and it is uncommitted. The owner runs matrix rows H2–H8. P02-02 was pushed and CI 40202761834 is green; its matrix rows D2–D5, M1–M6, T1–T2, S1–S2, I1 and A1 are still pending.
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
- **Last updated:** 2026-10-25 (P02-03 implemented; manual matrix pending)

## NEXT ACTION (ONE task only)
**P02-04 — Fullscreen detection with game-safe hide/restore; configurable exceptions; never assume an overlay works above exclusive-fullscreen apps.** (guide §5 P02-04, D06, §4.1; ADR 0009 feeds `FullscreenStarted`/`FullscreenEnded`)
1. **Platform.Windows `Win32FullscreenDetector` (guide §3 `IFullscreenDetector`, event-driven per D10):**
   - Sources:
     - `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`, plus location changes of the foreground window (`EVENT_OBJECT_LOCATIONCHANGE` filtered to that window, or a re-check on foreground change and on the display changes from ADR 0008)
     - `SHQueryUserNotificationState`: `QUNS_RUNNING_D3D_FULL_SCREEN`, `QUNS_PRESENTATION_MODE`, `QUNS_BUSY`
   - Fullscreen means the foreground window covers its monitor's full bounds and is not the shell or desktop (`Progman`, `WorkerW`, `Shell_TrayWnd`), not the island itself and not cloaked; or the notification state says D3D-fullscreen or presentation mode.
   - Per-monitor: hide only when the fullscreen window is on **the island's monitor**.
   - No polling loop. If a short re-check is needed, it is bounded and stops when idle.
2. **Core:** pure `FullscreenPolicy` — given the detector snapshot, the island's monitor, the `Behavior.AutoHideInFullscreen` setting and the exceptions, decide hide or show.
   - Exceptions are a setting `Behavior.FullscreenExceptions`: a list of process **executable names** (e.g. `vlc.exe`), stored locally and **never logged** (ADR 0004), validated (file-name characters only, ≤ 50 entries).
   - A new property with a default, so no schema bump is needed.
3. **App wiring:** detector events → `IIslandStateMachine.Fire(FullscreenStarted|FullscreenEnded)` on the UI thread, debounced at about 300 ms to avoid flicker while the user alt-tabs. Log state changes only as "fullscreen on/off"; never log window titles or process names.
4. **Docs:** state honestly in ADR/README that **exclusive-fullscreen games may draw above any overlay**. The island hides itself in that case; it never tries to force itself on top.
5. **Tests:**
   - Unit: policy (monitor match, exceptions, setting off) and the exception-list validator.
   - Integration: detector on the real desktop with a helper borderless window sized to the monitor bounds (a child process or test window) → `FullscreenStarted`; close it → `FullscreenEnded`.
   - Smoke: open the helper fullscreen window → island window hidden (`IsWindowVisible` false); close it → visible again in the previous state.
   - Manual matrix rows: a video player in fullscreen, a borderless game, an exclusive-fullscreen game, PowerPoint presentation mode, F11 in a browser, and a fullscreen window on the other monitor (the island stays visible).
6. **Verify:** Release + Debug builds with 0 warnings and all tests green; CI green after the owner pushes. Do not commit.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | DONE | **G01 PASS** 2026-10-25, CI run 40201274900 (Release + Debug). See the G01 evidence below. |
| P02 — Island shell | P02-01…P02-05 | IN_PROGRESS (01 DONE; 02, 03 IMPLEMENTED_AWAITING_VALIDATION) | G02 pending (needs a real multi-DPI/multi-monitor check) |
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
- 2026-10-25 P02-01 DONE: island window, Win32 monitor service, placement calculator, PerMonitorV2 manifest (ADR 0008). Details are in WORK_LOG.
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
| 2026-10-25 | P02-03 notch | Windows 11 Pro 25H2 x64, one monitor at 100 % | Release and Debug build + `dotnet test --solution` | PASS | 0 warnings; Release 299/299, Debug 300/300 before the geometry tests; Integration 29/29 with `NotchGeometryTests` |
| 2026-10-25 | P02-03 notch | same, real desktop | Real app screenshot (2× zoom) collapsed, then UIA Expand screenshot | PASS | Window `L=750 T=0 420×180`. The notch is flush with the top edge, with shoulders, speaker slit, camera lens and sensor dot. Expanded, it grows down with the title, placeholder and pin. |
| 2026-10-25 | P02-02 | GitHub Actions `windows-2025` | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40202761834 |
| 2026-10-25 | P02-03 | Windows 11 Pro 25H2 x64, one monitor at 100 % | `dotnet build` + `dotnet test --solution` for Release and Debug, `CI=true` | PASS | 0 warnings (an earlier attempt failed on IDE0005; fixed); Release 299/299, Debug 300/300; no stray processes |
| 2026-10-25 | P02-03 | same, real desktop | Real app driven through UI Automation from PowerShell: Expand → Toggle (pin); screenshots of collapsed, expanded and pinned | PASS | Window stays 420×180; collapsed capsule with dot; expanded panel with title and placeholder; pinned shows a green pin. The first screenshot showed WPF's default light toggle; restyled for the dark theme. UIA name "Dynamic Island, expanded and pinned". |
| — | P02-03 | real mouse, keyboard, Narrator, reduced motion | `docs/TEST_MATRIX.md` rows H2–H8 | PENDING_WINDOWS | Owner to run; agent avoids synthetic input on the user's desktop |
| 2026-10-25 | P02-01 | GitHub Actions `windows-2025` | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40201946771 |
| 2026-10-25 | P02-02 | Windows 11 Pro 25H2 x64, one monitor 1920 px at 100 % | `dotnet build` + `dotnet test --solution` for Release and Debug | PASS | 0 warnings (an earlier attempt failed on CA1001; fixed); Release 200/200, Debug 201/201 |
| 2026-10-25 | P02-02 | same, real desktop | Smoke `Only_the_visible_pill_receives_clicks` | PASS | `WindowFromPoint`: pill center belongs to the island, top-left corner pixel does not (click-through) |
| 2026-10-25 | P02-02 | same | Icon preview rendered at 16/24/32/48/64/256 px; `ExtractIconExW` count ≥ 1 for both exes | PASS | Crisp original dark tile, light capsule, centered green dot. The first generator run produced a 142-byte file (PowerShell unrolled the byte arrays); fixed. |
| 2026-10-25 | P02-02 | same | Real app: `taskkill /IM DynamicIsland.exe` (no `/F`) | PASS | Clean exit, code 0; log shows `Island placed: primary monitor True, fallback False, scale 1, size 200x36 px` |
| — | P02-02 | multi-DPI, multi-monitor, taskbar, sleep/wake | `docs/TEST_MATRIX.md` rows D2–D5, M1–M6, T1–T2, S1–S2, I1, A1 | PENDING_WINDOWS | Owner to run on real hardware; a single 100 % monitor is visible to the agent |
| — | P02-01 | multi-monitor, mixed DPI, 125–200 % | Placement on a secondary or high-DPI monitor, fallback when unplugged | PENDING_WINDOWS | Single 100 % monitor on the dev host; covered by unit tests; G02 matrix in P02-02 || 2026-10-25 | P01-04 | GitHub Actions `windows-2025`, owner push | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40200547110 |

## BLOCKERS / RISKS
- Enterprise policy may block an unsigned per-user executable; never bypass it.
- Self-update and rollback must handle a read-only portable folder and DB schema compatibility (P06).
- The dev host is one security patch behind (runtime 10.0.12 / SDK 10.0.401 vs 10.0.13 / 10.0.402). The owner should update before any release build.
- Packaging (P06) must ship `DynamicIsland.Launcher.exe` next to `DynamicIsland.exe`, and shortcuts and startup target the launcher (ADR 0006).
- G02 requires multi-monitor, DPI 100–200 %, unplug/rotate and sleep/wake checks on real hardware. The dev host has one monitor visible to the agent, so the owner may need to run part of that matrix.

## ARCHITECTURE DECISIONS / ADRS
- Frozen D01–D15: see guide §0. No deviations approved.
- 0001 supported Windows versions · 0002 solution/build/test platform · 0003 settings persistence (consent defaults `NotAsked`; schema-bump rule for new enum values) · 0004 local logging and redaction · 0005 lifecycle, exit codes 0/1/3, single instance with acknowledged pipe · 0006 crash supervision, Safe Mode, Debug-only test hooks, launcher as the entry point · 0007 component error boundaries, notices (keys only), user-reviewed diagnostic export · 0008 island window (tool window + PerMonitorV2), monitor identity (device interface path, never logged), placement calculator, display-change debounce · 0009 state machine (exhaustive table), fixed-size window with animated island, motion preference, keyboard/focus return, UI Automation patterns; amended: notch design (flush top, shoulders, sensor housing).
- Pending: per-user installer technology and updater trust model (P06).

## HANDOFF NOTES
- Read `AGENTS.md` first; guide §5 has the task definitions. Current next task: **P02-04**.
- Widgets and providers (P04) must be `IIsolatedComponent`s registered as optional (ADR 0007). They must honor Safe Mode (ADR 0006) and never log free text (ADR 0004).
- If code is present that this file does not describe, inspect git and tests and reconcile this file before editing.
