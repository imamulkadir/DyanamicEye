# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01 is done (G01 PASS, confirmed on CI run 40201274900). P02-01 is done; P02-02..P02-05 are implemented; **G02 waits for the owner's hardware matrix**.
- **Active task:** G02 sign-off (owner hardware matrix), then P03-01.
- **Active task status:** BLOCKED on owner validation (G02). P02-05 is IMPLEMENTED_AWAITING_VALIDATION and uncommitted; it includes the fix for the P02-04 CI failure (run 40205375218).
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
- **Last updated:** 2026-10-25 (P02-05 implemented; G02 pending owner)

## NEXT ACTION (ONE task only)
**G02 sign-off: the owner runs the manual hardware rows; the agent fixes anything that fails. Then start P03-01.** (guide §5 "do not skip gates")
1. **Owner:** run every `NOT_RUN` row in `docs/TEST_MATRIX.md` → G02 section (D2–D5, M1–M6, T1–T2, S1–S2, H2–H8, F1–F8, P2, I1, A1) on real hardware, and report PASS/FAIL per row. N/A is fine where hardware is missing, for example no second monitor or no battery; give the reason.
2. **Agent:** record the results in `TEST_MATRIX.md` and TEST EVIDENCE. Fix each FAIL in the owning task (P02-01..05) with a regression test. Re-run the full suite with `--max-parallel-test-modules 1` in both configs and check CI.
3. When every row is PASS or a justified N/A: mark P02-02..P02-05 DONE, record **G02 PASS** with the evidence, and set NEXT ACTION to **P03-01** (tray icon and menu; guide §5 P03, D01–D02, §4.4). Reconfirm the P03 task list from the guide at that point.

If the owner explicitly approves continuing P03 while G02 is pending, record that approval with its date here and keep G02 open.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | DONE | **G01 PASS** 2026-10-25, CI run 40201274900 (Release + Debug). See the G01 evidence below. |
| P02 — Island shell | P02-01…P02-05 | IN_PROGRESS (01 DONE; 02–05 IMPLEMENTED_AWAITING_VALIDATION) | G02 pending (needs a real multi-DPI/multi-monitor check) |
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

## TEST EVIDENCE (current task; older rows in `docs/WORK_LOG.md`)
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P02-04 | GitHub Actions `windows-2025` | CI workflow | **FAIL** | https://github.com/imamulkadir/DyanamicEye/actions/runs/40205375218: `Island_hides_while_a_fullscreen_window_is_in_front_and_returns_afterwards`, 329/330 |
| 2026-10-25 | P02-05 | Windows 11 Pro 25H2 x64, 1 monitor at 100 % | Reproduced the CI failure locally (Release suite) | FAIL → fixed | The app log showed hooks installed but no fullscreen detection. Cause: the new window was DWM-cloaked during the foreground event. Fix: uncloak hook + one-shot follow-up. Release smoke 3/3 afterwards. |
| 2026-10-25 | P02-05 | same, `CI=true` | `dotnet test --solution … --max-parallel-test-modules 1`, Release and Debug ×2; plus Release in parallel mode ×2 | PASS | Release 342/342 ×4, Debug 343/343 ×2, **0 skipped**, no stray processes |
| 2026-10-25 | P02-05 | same | Idle budget smoke: 10 s idle, collapsed | PASS | **0 ms CPU**, working set 94 MiB |
| 2026-10-25 | P02-05 | same | Visual references generated and reviewed (composite preview of the 4 states) | PASS | Notch outline, shoulders, sensor housing, expanded panel and green pin correct |
| — | G02 | real hardware | `TEST_MATRIX.md` G02 NOT_RUN rows | PENDING_WINDOWS | Owner |
| 2026-10-25 | P02-03 notch | GitHub Actions `windows-2025` | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40204446036 |
| 2026-10-25 | P02-04 | Windows 11 Pro 25H2 x64, one monitor at 100 % | `dotnet test --solution` (first run) | FAIL → fixed | Release: the fullscreen smoke test did not hide; Debug: the corner click-through failed. Cause: integration helper windows were topmost and covered the screen in parallel. Fixed with `topmost: false`. |
| 2026-10-25 | P02-04 | same | `dotnet build` + `dotnet test --solution` for Release and Debug, twice each | PASS | 0 warnings. Release 330/330 ×2 (end-to-end fullscreen hide and return **passed**). Debug 330 passed + 1 **skipped** ×2: Windows refused foreground to the test window, reported as a skip. |
| 2026-10-25 | P02-04 | same | Integration `Win32FullscreenDetectorTests` on real windows | PASS | Covering window → fullscreen on the primary monitor with the exe name; half-size, maximized-with-caption and own-process windows → not fullscreen |
| — | P02-04 | real video, game, browser F11, presentation, 2 monitors | `docs/TEST_MATRIX.md` rows F1–F8 | PENDING_WINDOWS | Owner to run |
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
- 0001 supported Windows versions · 0002 solution/build/test platform · 0003 settings persistence (consent defaults `NotAsked`; schema-bump rule for new enum values) · 0004 local logging and redaction · 0005 lifecycle, exit codes 0/1/3, single instance with acknowledged pipe · 0006 crash supervision, Safe Mode, Debug-only test hooks, launcher as the entry point · 0007 component error boundaries, notices (keys only), user-reviewed diagnostic export · 0008 island window (tool window + PerMonitorV2), monitor identity (device interface path, never logged), placement calculator, display-change debounce · 0009 state machine (exhaustive table), fixed-size window with animated island, motion preference, keyboard/focus return, UI Automation patterns; amended: notch design (flush top, shoulders, sensor housing) · 0010 fullscreen detection (WinEvent hooks, covers-monitor rule, per-monitor policy, exe-name exceptions, honest exclusive-fullscreen limit; amended: uncloak hook + one-shot follow-up) · 0011 idle activity budget, power-aware motion policy, offscreen visual regression with own references, sequential test modules.
- Pending: per-user installer technology and updater trust model (P06).

## HANDOFF NOTES
- Read `AGENTS.md` first; guide §5 has the task definitions. Current next task: **G02 sign-off by the owner, then P03-01**.
- Widgets and providers (P04) must be `IIsolatedComponent`s registered as optional (ADR 0007). They must honor Safe Mode (ADR 0006) and never log free text (ADR 0004).
- If code is present that this file does not describe, inspect git and tests and reconcile this file before editing.
