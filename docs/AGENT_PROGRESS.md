# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is not a generic task list; it is the durable checkpoint allowing new agents to resume after context reset. Update alongside source changes. Do not mark anything DONE without evidence.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS — P00-01..P00-03 done. Solution skeleton builds and tests pass. No product features yet.
- **Active task:** P00-04 — GitHub Actions Windows CI.
- **Active task status:** NOT_STARTED
- **Last passed gate:** None
- **Current branch / commit:** Repo created by owner (2026-10-25). **Owner reviews and makes all commits. Agents must NOT commit.** Report changed files and the owner commits them.
- **Host (P00-02, verified):** Windows 11 Pro 25H2, build 26200.9711, x64 (RID `win-x64`). .NET SDK 10.0.401 (pinned) + 8.0.425; runtimes NETCore/WindowsDesktop/AspNetCore 10.0.12 + 8.0.31; x86 dotnet also present.
- **Supported OS policy:** see `docs/ADR/0001-supported-windows-versions.md` (tested target Win11 24H2/25H2 x64; Win10 22H2 Home/Pro NOT supported by .NET 10).
- **Build/test commands:** `dotnet restore DynamicIsland.sln --locked-mode`; `dotnet build DynamicIsland.sln -c Release --no-restore`; `dotnet test --solution DynamicIsland.sln -c Release` (Microsoft.Testing.Platform runner via `global.json`; legacy VSTest-style `dotnet test` fails by design). The smoke test opens the real app window on the desktop.
- **Last updated:** 2026-10-25 (P00-03 done)

## NEXT ACTION (ONE task only)
**P00-04 — GitHub Actions Windows CI.** (guide §5 P00-04, G00)
1. Create `.github/workflows/ci.yml` (remove `.github/workflows/.gitkeep`) with triggers `push` to `main` and `pull_request`, plus `permissions: contents: read`, a `concurrency` group, and the `windows-latest` runner (or a pinned `windows-2025`).
2. Steps: `actions/checkout` and `actions/setup-dotnet` **pinned by full commit SHA** (verify current SHAs/versions from the official repos), with `global-json-file: global.json`. Then `dotnet restore DynamicIsland.sln --locked-mode`, `dotnet build DynamicIsland.sln -c Release --no-restore`, and `dotnet test --solution DynamicIsland.sln -c Release --no-build`. The env sets `CI=true` and `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`.
3. Smoke tests: windows-latest runners have a desktop session, so run all tests. If the window smoke test proves flaky in CI, split it into a separate job and record that. Upload test results (`TestResults`) only as diagnostics. **Do not publish or describe any build artifact as a trusted release.**
4. Validate the YAML locally (structure review; `actionlint` only if already available, no installs). Real CI evidence needs the owner to push to GitHub, so mark the task `IMPLEMENTED_AWAITING_VALIDATION` until the owner reports a green run, and record the run URL.
5. Update this file. G00 passes only after a green fresh-clone CI run. Do not commit; list changed files for the owner.

**Expected verification:** workflow file reviewed; local commands identical to CI pass; G00 pending the owner's CI run.

## TASK BOARD (compact; detailed definitions live in guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | IN_PROGRESS (P00-01..03 DONE) | G00 pending (needs CI run) |
| P01 — Reliability kernel | P01-01…P01-05 | NOT_STARTED | G01 pending |
| P02 — Island shell | P02-01…P02-05 | NOT_STARTED | G02 pending |
| P03 — Tray/settings/startup | P03-01…P03-05 | NOT_STARTED | G03 pending |
| P04 — Core widgets/data | P04-01…P04-07 | NOT_STARTED | G04 pending |
| P05 — Customization/performance | P05-01…P05-04 | NOT_STARTED | G05 pending |
| P06 — Packaging/updates | P06-01…P06-09 | NOT_STARTED | G06 pending |
| P07 — Release validation | P07-01…P07-05 | NOT_STARTED | G07 pending |

## RECENT VERIFIED CHANGES (newest first; max 10)
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
- Current next task: **P00-04**. The solution skeleton exists; P01 builds on `AppHost` and the empty Core/Infrastructure projects. Do not jump to UI implementation.
- Owner handles git repo creation and all commits (owner instruction 2026-10-25). P00-04 (GitHub Actions) needs the owner's GitHub repo URL; workflow file can be authored without it.
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
