# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01-01..P01-04 are done; P01-05 is next.
- **Active task:** P01-05: Windows integration error boundaries, nonfatal error display contracts, diagnostic export preview.
- **Active task status:** NOT_STARTED. P01-04 is DONE but uncommitted, awaiting owner review and push.
- **Last passed gate:** G00 (2026-10-25, CI run https://github.com/imamulkadir/DyanamicEye/actions/runs/40196393380).
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
- **Last updated:** 2026-10-25 (P01-04 DONE)

## NEXT ACTION (ONE task only)
**P01-05 — Windows integration error boundaries, nonfatal error display contracts, diagnostic export preview.** (guide §5 P01-05, §4.2, §4.5, D14; G01 "simulated widget failure isolated")
1. **Error boundary (Core, pure):** a component/provider runner that executes bounded async work with a timeout and `CancellationToken`.
   - Classify failures per §4.5: transient means retry with exponential backoff + jitter and a cap; recoverable means isolate and reinitialize; fatal means escalate to `LifecycleCoordinator.RequestShutdown(Fatal)`.
   - Expose a per-component status (`Healthy` / `Degraded` / `Failed`) with a user-safe reason code. Never expose exception messages to UI.
   - No fire-and-forget: every background task is observed.
2. **Nonfatal notices (Core contract + in-memory implementation):** `INotificationService` (guide §3) publishes `UserNotice` records (severity, stable message key, optional action id) for the shell/tray UI (P02/P03) to render later.
   - De-duplicate repeated notices and rate-limit them.
   - Notices never contain personal data or raw exception text.
3. **Diagnostic export preview (Infrastructure):** `DiagnosticBundleBuilder` builds a **preview** for user review; nothing is written or uploaded without a later explicit user action (P03 UI, D14).
   - Contents: app version, OS version/architecture, Safe Mode, settings load outcome, recent crash timestamps, and the last N lines of the redacted app and launcher logs.
   - Re-run `LogRedactor` on all text. Exclude settings values that could be personal.
   - Add `WriteZipAsync(previewApproved, path)` with a size cap. Its manifest lists every included file.
4. **App wiring:** register the services in `AppHost`. Respect `LaunchArguments.SafeMode`: optional components are not started.
5. **Tests:**
   - a fake widget/provider that throws or hangs is isolated, its status is `Failed`/`Degraded`, other components keep running and the app is not shut down
   - backoff schedule bounds
   - fatal escalation requests shutdown
   - notice de-duplication and rate limiting
   - the preview contains no user name, profile path or token (seed logs with them)
   - the zip contains exactly the previewed files
6. **Verify:** Release + Debug builds with 0 warnings; all tests green in both; CI green after the owner pushes. If G01 is satisfied, record G01 PASS evidence here. Do not commit.

**Expected verification:** the G01 items are all proven: corrupt settings recover; simulated widget failure is isolated; deliberate Exit never auto-relaunches; third crash does not loop; no duplicate app.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | IN_PROGRESS (01..04 DONE) | G01 pending P01-05. Proven so far: corrupt settings recover (tests + real run); no duplicate app (smoke); Exit never relaunches and the 3rd crash does not loop (unit + Debug end-to-end launcher smoke). |
| P02 — Island shell | P02-01…P02-05 | NOT_STARTED | G02 pending |
| P03 — Tray/settings/startup | P03-01…P03-05 | NOT_STARTED | G03 pending |
| P04 — Core widgets/data | P04-01…P04-07 | NOT_STARTED | G04 pending |
| P05 — Customization/performance | P05-01…P05-04 | NOT_STARTED | G05 pending |
| P06 — Packaging/updates | P06-01…P06-09 | NOT_STARTED | G06 pending |
| P07 — Release validation | P07-01…P07-05 | NOT_STARTED | G07 pending |

## RECENT VERIFIED CHANGES (newest first; max 10; older detail in `docs/WORK_LOG.md`)
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
- 2026-10-25 P01-03 CI fix pushed (`c0c237f`, then the fix commit); CI run 40199779718 **success**. It added acknowledged activation IPC and a deterministic budget test (details in WORK_LOG).
- 2026-10-25 P01-03 DONE: lifecycle coordinator, session marker, single instance + activation pipe, exception policy (ADR 0005).
- 2026-10-25 P01-02 DONE: redacting rolling JSON-lines file logger (ADR 0004); CI 40198235944 success.
- 2026-10-25 P01-01 DONE: versioned settings store with atomic writes and recovery (ADR 0003); CI 40197494204 success.
- 2026-10-25 P00-01..04 DONE: repo baseline, SDK pin (ADR 0001), solution and build policy (ADR 0002), Windows CI; G00 PASS.

## TEST EVIDENCE (current task; older rows in `docs/WORK_LOG.md`)
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P01-03 fix | GitHub Actions `windows-2025` | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40199779718 |
| 2026-10-25 | P01-04 | Windows 11 Pro 25H2 26200.9711 x64, SDK 10.0.401 | `dotnet build DynamicIsland.sln -c Release` and `-c Debug` | PASS | 0 warnings each. Earlier attempts failed on CA1822 and CA1711; fixed. |
| 2026-10-25 | P01-04 | same | `dotnet test --solution DynamicIsland.sln -c Release\|Debug --no-build` (first run) | FAIL → fixed | 4 failures: 3 were a wrong test expectation (a second `RecordCrash` adds a crash), 1 was a real bug (history lost when the folder was unusable) |
| 2026-10-25 | P01-04 | same, `CI=true`, real desktop | Release and Debug, each run twice | PASS | Release 146/146, Debug 147/147 both times. No stray processes and no leftover temp data root. The real profile holds only the Release smoke logs (no crash history). |
| 2026-10-25 | P01-04 | same, Debug end-to-end | `CrashRecoverySmokeTests.Crash_loop_…`: launcher + app with `DYNAMICISLAND_TEST_CRASH=ui` in a temp data root | PASS | Launcher log shows 4 runs: Relaunch, Relaunch, RelaunchInSafeMode, then Stop (safe mode True). Launcher exit code 1. App log has one `SafeModeActive`. |
| 2026-10-25 | P01-04 | same, Release | `Release_build_ignores_the_test_crash_switch` | PASS | With the crash and data-root variables set, Release starts normally, exits 0 and creates no temp data root |

## BLOCKERS / RISKS
- Enterprise policy may block an unsigned per-user executable; never bypass it.
- Self-update and rollback must handle a read-only portable folder and DB schema compatibility (P06).
- The dev host is one security patch behind (runtime 10.0.12 / SDK 10.0.401 vs 10.0.13 / 10.0.402). The owner should update before any release build.
- Packaging (P06) must ship `DynamicIsland.Launcher.exe` next to `DynamicIsland.exe`, and shortcuts and sign-in startup target the launcher (ADR 0006).

## ARCHITECTURE DECISIONS / ADRS
- Frozen D01–D15: see guide §0. No deviations approved.
- 0001 supported Windows versions · 0002 solution/build/test platform · 0003 settings persistence (consent defaults `NotAsked`; schema-bump rule for new enum values) · 0004 local logging and redaction · 0005 lifecycle, exit codes 0/1/3, single instance with acknowledged pipe · 0006 crash supervision, Safe Mode, Debug-only test hooks, launcher as the entry point.
- Pending: per-user installer technology and updater trust model (P06).

## HANDOFF NOTES
- Read `AGENTS.md` first; guide §5 has the task definitions. Current next task: **P01-05**.
- Do not jump to UI (P02) before G01 passes.
- From P04 on, widgets and providers must honour `LaunchArguments.SafeMode` (ADR 0006 §5) and must never log free text (ADR 0004).
- If code is present that this file does not describe, inspect git and tests and reconcile this file before editing.
