# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is not a generic task list; it is the durable checkpoint allowing new agents to resume after context reset. Update alongside source changes. Do not mark anything DONE without evidence.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS — Phase P00 complete; gate G00 PASS. Starting P01 (reliability kernel).
- **Active task:** P01-03 — Lifecycle coordinator, single instance, activation IPC.
- **Active task status:** NOT_STARTED (P01-02 DONE, uncommitted, awaiting owner review; P01-01 pushed, CI green)
- **Last passed gate:** G00 (2026-10-25, CI run https://github.com/imamulkadir/DyanamicEye/actions/runs/40196393380)
- **Repository:** https://github.com/imamulkadir/DyanamicEye (public). CI status for any run can be read without auth: `Invoke-RestMethod https://api.github.com/repos/imamulkadir/DyanamicEye/actions/workflows/ci.yml/runs?per_page=1`.
- **Current branch / commit:** Repo created by owner (2026-10-25); default branch `master`. **Owner reviews and makes all commits. Agents must NOT commit.** Report changed files and the owner commits them.
- **Host (P00-02, verified):** Windows 11 Pro 25H2, build 26200.9711, x64 (RID `win-x64`). .NET SDK 10.0.401 (pinned) + 8.0.425; runtimes NETCore/WindowsDesktop/AspNetCore 10.0.12 + 8.0.31; x86 dotnet also present.
- **Supported OS policy:** see `docs/ADR/0001-supported-windows-versions.md` (tested target Win11 24H2/25H2 x64; Win10 22H2 Home/Pro NOT supported by .NET 10).
- **Build/test commands:** `dotnet restore DynamicIsland.sln --locked-mode`; `dotnet build DynamicIsland.sln -c Release --no-restore`; `dotnet test --solution DynamicIsland.sln -c Release` (Microsoft.Testing.Platform runner via `global.json`; legacy VSTest-style `dotnet test` fails by design). The smoke test opens the real app window on the desktop.
- **Last updated:** 2026-10-25 (P01-02 DONE)

## NEXT ACTION (ONE task only)
**P01-03 — App lifecycle coordinator: explicit shutdown vs. session end vs. crash, cancellation/disposal, single instance + activation IPC.** (guide §5 P01-03, §4.4, §4.5)
1. **Core (`Core/Lifecycle/`, pure, unit-tested):**
   - an `ExitReason` enum (`UserExit`, `SessionEnding`, `Restart`, `Fatal`)
   - `ILifecycleCoordinator`: one-shot `RequestShutdown(reason)`, an app-wide `CancellationToken` cancelled on shutdown, and an ordered async shutdown of registered components with a time budget (e.g. 5 s) that logs any component exceeding it
   - a clean-shutdown marker contract for P01-04: a clean exit clears the "session unhealthy" flag, a fatal exit leaves it set
2. **Single instance (`Platform.Windows`):**
   - a per-user named mutex `Local\DynamicIsland.{UserSid}`
   - a named pipe `DynamicIsland.{UserSid}.activate`, created with `NamedPipeServerStreamAcl.Create` and a `PipeSecurity` that allows only the current user SID (plus `PipeOptions.CurrentUserOnly`)
   - the protocol is one fixed short command (`activate`), with a length limit and timeout; reject anything else
   - a second instance sends `activate`, then exits with code 0 and **no** window; the first instance receives it and brings its window forward without forcing focus. Note that the shell is still the dev window until P02.
3. **App wiring (`App.xaml.cs` → thin; logic in the coordinator):**
   - startup order: acquire single instance, build host, load settings (`ISettingsStore.LoadAsync`; log the `Outcome` and the correction count only), show the window
   - `SessionEnding` → `ExitReason.SessionEnding`, which shuts down promptly
   - unhandled exceptions: `DispatcherUnhandledException`, `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` are logged as Critical, the session is marked unhealthy and the app exits with a non-zero code. **Never set `Handled = true` unconditionally** (guide §0.2).
4. **Tests:**
   - Unit: coordinator ordering, timeout, the one-shot request, the cancellation token, the marker set/clear contract
   - Integration (Windows): mutex contention between two processes or handles; pipe round-trip (`activate`); invalid messages rejected; the ACL denies other users (assert the security descriptor owner/rules)
   - Smoke: launching the exe twice leaves one process, the second exits 0; closing leaves no process
5. **Verify:** Release build with 0 warnings; full test suite green; manual check that a double launch shows one window. Update this file; list changed files; do not commit.

**Expected verification:** the G01 items "no duplicate app" and "deliberate Exit is distinguishable from a crash" are proven by tests.

## TASK BOARD (compact; detailed definitions live in guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | IN_PROGRESS (P01-01, P01-02 DONE) | G01 pending (corrupt-settings recovery covered by P01-01 tests) |
| P02 — Island shell | P02-01…P02-05 | NOT_STARTED | G02 pending |
| P03 — Tray/settings/startup | P03-01…P03-05 | NOT_STARTED | G03 pending |
| P04 — Core widgets/data | P04-01…P04-07 | NOT_STARTED | G04 pending |
| P05 — Customization/performance | P05-01…P05-04 | NOT_STARTED | G05 pending |
| P06 — Packaging/updates | P06-01…P06-09 | NOT_STARTED | G06 pending |
| P07 — Release validation | P07-01…P07-05 | NOT_STARTED | G07 pending |

## RECENT VERIFIED CHANGES (newest first; max 10)
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

## TEST EVIDENCE
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

## BLOCKERS / RISKS
- No target Windows development/testing host verified yet. Do not assert WPF UI tests passed.
- Enterprise policy may block unsigned/per-user executable; never bypass.
- Self-update/rollback architecture must validate portable read-only folder and DB schema compatibility.
- Dev host is one security patch behind: runtime 10.0.12 / SDK 10.0.401 vs latest 10.0.13 / SDK 10.0.402 (2026-10-13 security release). Owner should install SDK 10.0.402 before any release build. Agent did not install it (system-wide change).

## ARCHITECTURE DECISIONS / ADRS
- Frozen D01–D15: see guide §0. No deviations approved.
- ADR 0001 (Accepted): supported Windows versions. It clarifies guide §0 and does not deviate from it.
- ADR 0002 (Accepted): `.sln` format, per-project TFMs, build/lock policy, xUnit v3 on Microsoft.Testing.Platform, host without default sources.
- ADR 0004 (Accepted): in-house JSON-lines file logger (no Serilog), retention/caps, non-blocking queue, fail-safe, redaction scope; callers must never log free text.
- ADR 0003 (Accepted): settings model and consent defaults (startup and update checks are `NotAsked` and asked in P03), atomic save + `.bak`, quarantine, read-only for newer schemas, migrations, **schema-bump rule for new enum values**, reflection-based STJ.
- Pending ADRs: concrete per-user installer technology and updater trust/provenance model when P06 begins.

## HANDOFF NOTES
- Read `AGENTS.md` first; guide §5 holds task-by-task definitions.
- Current next task: **P01-03**. P01 builds on `AppHost` and the empty Core/Infrastructure projects. Do not jump to UI implementation.
- Owner handles git repo creation and all commits (owner instruction 2026-10-25). The owner pushed P00-03 on 2026-10-25. `gh` is not installed, but the repo is public, so CI run status can be read through the GitHub REST API without auth (see CURRENT STATUS).
- If a fresh agent finds code already present, inspect git + tests and reconcile this file before editing anything.

## SESSION UPDATE TEMPLATE (copy values into sections above, don't append endless copies)
```text
Date/agent:
Task ID/status before -> after:
Files changed:
Exact tests and host OS: [PASS/FAIL/NOT_RUN]
Functional validation: [PASS/FAIL/PENDING_WINDOWS]
Commit hash:
New decisions/ADRs:
Blockers:
NEXT ACTION (one task, exact paths and commands):
```
