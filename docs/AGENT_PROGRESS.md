# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01 is done (G01 PASS locally; CI confirmation comes on the owner's push of P01-05). P02 is next.
- **Active task:** P02-01: island window + monitor placement service.
- **Active task status:** NOT_STARTED. P01-05 is DONE but uncommitted, awaiting owner review and push. **First confirm that P01-05 CI is green.**
- **Last passed gate:** G01 (local, 2026-10-25); G00 (CI run https://github.com/imamulkadir/DyanamicEye/actions/runs/40196393380).
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
- **Last updated:** 2026-10-25 (P01-05 DONE, G01 PASS locally)

## NEXT ACTION (ONE task only)
**P02-01 — Island window and monitor placement service.** Top-center default, configurable offsets, work area, display ID persistence, primary fallback. (guide §5 P02-01, §4.1 focus/click rules, D05, D08)
1. **Core (pure, unit-tested):**
   - the `IMonitorService` contract (guide §3) with `MonitorInfo` (stable `Id`, physical `Bounds` and `WorkArea`, `DpiScale`, `IsPrimary`) and a `MonitorsChanged` event
   - `IslandPlacementCalculator`: monitors + `PreferredMonitorId` + `IslandPlacement` + offsets + island size in DIPs → target monitor and physical rectangle inside the **work area**. It falls back to the primary monitor, then the first monitor, when the preferred one is missing. It clamps to stay fully visible.
   - Add settings `Appearance.HorizontalOffset` / `VerticalOffset` (DIPs, clamped). Under ADR 0003 rule 7 these are new properties with defaults, so no schema bump is needed.
2. **Platform.Windows:** `Win32MonitorService` using `EnumDisplayMonitors` + `GetMonitorInfo` (`MONITORINFOEXW`) + `GetDpiForMonitor`. Derive a stable device id via `EnumDisplayDevices` (device interface name), not the volatile `\\.\DISPLAYn` alone. Raise `MonitorsChanged` on `WM_DISPLAYCHANGE` / `WM_DPICHANGED` / `WM_SETTINGCHANGE(SPI_SETWORKAREA)`; the listener lives in the shell window.
3. **App: island window** replacing the dev `MainWindow`.
   - Borderless, transparent, topmost, not shown in the taskbar or Alt+Tab (`WS_EX_TOOLWINDOW`), without stealing focus on show (`ShowActivated=false`). Keyboard focus must still work when clicked (§4.1).
   - Placed with `SetWindowPos` in physical pixels. Add `app.manifest` with **PerMonitorV2** DPI awareness.
   - Draw a simple collapsed pill with original styling (no copied assets). The state machine is P02-03.
   - Keep single-instance activation working.
4. **Tests:**
   - Unit: calculator across 1–2 monitors, negative-coordinate monitors, mixed DPI (100/150 %), taskbar-reduced work areas, a missing preferred monitor (falls back), offsets and clamping.
   - Integration: `Win32MonitorService` on the real host returns at least one primary monitor whose work area is inside its bounds.
   - Smoke: the app window appears, top-centered within ±2 px of the expected rectangle on the primary monitor (compare `GetWindowRect` with the calculator), and is not in the taskbar.
5. **Verify:** Release + Debug builds with 0 warnings and all tests green. Do a manual check on this host at the current DPI and record it. Multi-monitor and DPI matrix checks belong to G02, so mark what can't be checked on one monitor as `PENDING_WINDOWS`. Do not commit.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | DONE | **G01 PASS (local)** 2026-10-25; CI confirmation pending the owner's P01-05 push. See the G01 evidence below. |
| P02 — Island shell | P02-01…P02-05 | NOT_STARTED | G02 pending (needs a real multi-DPI/multi-monitor check) |
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
| 2026-10-25 | P01-04 | GitHub Actions `windows-2025`, owner push | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40200547110 |
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
- 0001 supported Windows versions · 0002 solution/build/test platform · 0003 settings persistence (consent defaults `NotAsked`; schema-bump rule for new enum values) · 0004 local logging and redaction · 0005 lifecycle, exit codes 0/1/3, single instance with acknowledged pipe · 0006 crash supervision, Safe Mode, Debug-only test hooks, launcher as the entry point · 0007 component error boundaries, notices (keys only), user-reviewed diagnostic export.
- Pending: per-user installer technology and updater trust model (P06).

## HANDOFF NOTES
- Read `AGENTS.md` first; guide §5 has the task definitions. Current next task: **P02-01**.
- Widgets and providers (P04) must be `IIsolatedComponent`s registered as optional (ADR 0007). They must honor Safe Mode (ADR 0006) and never log free text (ADR 0004).
- If code is present that this file does not describe, inspect git and tests and reconcile this file before editing.
