# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is not a generic task list; it is the durable checkpoint allowing new agents to resume after context reset. Update alongside source changes. Do not mark anything DONE without evidence.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS — P00-01..P00-03 done. P00-04 CI workflow is written and awaits its first GitHub run.
- **Active task:** P00-04 — GitHub Actions Windows CI.
- **Active task status:** IMPLEMENTED_AWAITING_VALIDATION (owner pushed P00-03; the CI workflow is not yet pushed or run)
- **Last passed gate:** None
- **Current branch / commit:** Repo created by owner (2026-10-25); default branch `master`. **Owner reviews and makes all commits. Agents must NOT commit.** Report changed files and the owner commits them.
- **Host (P00-02, verified):** Windows 11 Pro 25H2, build 26200.9711, x64 (RID `win-x64`). .NET SDK 10.0.401 (pinned) + 8.0.425; runtimes NETCore/WindowsDesktop/AspNetCore 10.0.12 + 8.0.31; x86 dotnet also present.
- **Supported OS policy:** see `docs/ADR/0001-supported-windows-versions.md` (tested target Win11 24H2/25H2 x64; Win10 22H2 Home/Pro NOT supported by .NET 10).
- **Build/test commands:** `dotnet restore DynamicIsland.sln --locked-mode`; `dotnet build DynamicIsland.sln -c Release --no-restore`; `dotnet test --solution DynamicIsland.sln -c Release` (Microsoft.Testing.Platform runner via `global.json`; legacy VSTest-style `dotnet test` fails by design). The smoke test opens the real app window on the desktop.
- **Last updated:** 2026-10-25 (P00-04 implemented, awaiting CI run)

## NEXT ACTION (ONE task only)
**Close P00-04 / gate G00 — get the first CI result from the owner.**
1. The owner commits and pushes `.github/workflows/ci.yml` and the doc updates. The workflow triggers on push to `master` (the owner's default branch), on PRs, and manually (`workflow_dispatch`).
2. The owner reports the Actions run URL and its result. The agent cannot query it, because `gh` CLI is not installed on the host.
3. **Green:** record the URL and date in TEST EVIDENCE, set P00-04 to DONE and G00 to PASS (green run from a fresh checkout on the Windows runner). Then the next task is **P01-01**, typed settings: schema/version, defaults, validation, corruption fallback, atomic writes, migration tests (guide §5 P01, §4.3).
4. **Red:** diagnose from the step log and the `test-results` artifact. A likely suspect is the WindowsSmoke window test on the runner desktop. If it is runner-environment flakiness, move it to a separate job with `continue-on-error` only after recording why. Never mask real failures.

**Expected verification:** a green GitHub Actions run on `windows-2025` covering locked restore, warnings-as-errors build and 14/14 tests.

## TASK BOARD (compact; detailed definitions live in guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | IN_PROGRESS (P00-01..03 DONE; P00-04 IMPLEMENTED_AWAITING_VALIDATION) | G00 pending (needs green CI run) |
| P01 — Reliability kernel | P01-01…P01-05 | NOT_STARTED | G01 pending |
| P02 — Island shell | P02-01…P02-05 | NOT_STARTED | G02 pending |
| P03 — Tray/settings/startup | P03-01…P03-05 | NOT_STARTED | G03 pending |
| P04 — Core widgets/data | P04-01…P04-07 | NOT_STARTED | G04 pending |
| P05 — Customization/performance | P05-01…P05-04 | NOT_STARTED | G05 pending |
| P06 — Packaging/updates | P06-01…P06-09 | NOT_STARTED | G06 pending |
| P07 — Release validation | P07-01…P07-05 | NOT_STARTED | G07 pending |

## RECENT VERIFIED CHANGES (newest first; max 10)
- 2026-10-25 P00-04 IMPLEMENTED_AWAITING_VALIDATION (uncommitted; owner commits). Added `.github/workflows/ci.yml` and removed `.github/workflows/.gitkeep`.
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
| — | P00-04 | GitHub Actions `windows-2025` | First CI run | NOT_RUN | Waiting for the owner to push and report the run URL |

## BLOCKERS / RISKS
- No target Windows development/testing host verified yet. Do not assert WPF UI tests passed.
- Enterprise policy may block unsigned/per-user executable; never bypass.
- Self-update/rollback architecture must validate portable read-only folder and DB schema compatibility.
- Dev host is one security patch behind: runtime 10.0.12 / SDK 10.0.401 vs latest 10.0.13 / SDK 10.0.402 (2026-10-13 security release). Owner should install SDK 10.0.402 before any release build. Agent did not install it (system-wide change).

## ARCHITECTURE DECISIONS / ADRS
- Frozen D01–D15: see guide §0. No deviations approved.
- ADR 0001 (Accepted): supported Windows versions. It clarifies guide §0 and does not deviate from it.
- ADR 0002 (Accepted): `.sln` format, per-project TFMs, build/lock policy, xUnit v3 on Microsoft.Testing.Platform, host without default sources.
- Pending ADRs: concrete per-user installer technology and updater trust/provenance model when P06 begins.

## HANDOFF NOTES
- Read `AGENTS.md` first; guide §5 holds task-by-task definitions.
- Current next task: close P00-04 using the owner's CI result, then **P01-01**. P01 builds on `AppHost` and the empty Core/Infrastructure projects. Do not jump to UI implementation.
- Owner handles git repo creation and all commits (owner instruction 2026-10-25). The owner pushed P00-03 on 2026-10-25. The agent cannot see CI results because `gh` is not installed; the owner reports them.
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
