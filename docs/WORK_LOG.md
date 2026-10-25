# WORK_LOG.md — Archived agent history

Verbatim detail moved out of `docs/AGENT_PROGRESS.md` to keep startup context small (AGENTS.md). Newest entries first within each section. Append new archives at the top of the relevant section.

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
