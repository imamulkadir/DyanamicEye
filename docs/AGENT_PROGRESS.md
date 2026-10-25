# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is not a generic task list; it is the durable checkpoint allowing new agents to resume after context reset. Update alongside source changes. Do not mark anything DONE without evidence.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS — P00-01, P00-02 done; no product code yet.
- **Active task:** P00-03 — Solution/projects, DI host, analyzers, test projects.
- **Active task status:** NOT_STARTED
- **Last passed gate:** None
- **Current branch / commit:** No repository yet. **Owner will create the git repo and make all commits. Agents must NOT run git commands or commit.** Report changed files and the owner commits them.
- **Host (P00-02, verified):** Windows 11 Pro 25H2, build 26200.9711, x64 (RID `win-x64`). .NET SDK 10.0.401 (pinned) + 8.0.425; runtimes NETCore/WindowsDesktop/AspNetCore 10.0.12 + 8.0.31; x86 dotnet also present.
- **Supported OS policy:** see `docs/ADR/0001-supported-windows-versions.md` (tested target Win11 24H2/25H2 x64; Win10 22H2 Home/Pro NOT supported by .NET 10).
- **Last updated:** 2026-10-25 (P00-02 done)

## NEXT ACTION (ONE task only)
**P00-03 — Create solution, projects, dependency boundaries, DI host, analyzers, tests.** (guide §5 P00-03, §2, §3)
1. Add `.gitattributes` (`* text=auto`, `*.cs/*.xaml/*.csproj/*.sln* eol=crlf`) to stop LF/CRLF warnings.
2. `Directory.Build.props`: `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`, `Deterministic=true`, `ContinuousIntegrationBuild` when `CI`, `RestorePackagesWithLockFile=true`, `LangVersion=latest`. `Directory.Packages.props` with central package management (pinned versions). Root `.editorconfig`.
3. `dotnet new sln -n DynamicIsland` (use `.slnx` if SDK default; record choice). Projects under `src/` per guide §3: `App` (wpf, `net10.0-windows`, Generic Host + DI composition only), `Core` (classlib `net10.0`, **no WPF/Windows refs**), `Platform.Windows` and `Infrastructure` (classlib `net10.0-windows`/`net10.0` → Core), `Widgets` (→ Core), `Launcher`, `Updater` (console, minimal `Main` returning nonzero "not implemented" — no fake functionality). Tests: `UnitTests`, `IntegrationTests`, `WindowsSmokeTests` (xUnit, pinned). Remove `.gitkeep` from populated dirs.
4. Add architecture unit test asserting `DynamicIsland.Core` references no WPF/Windows assemblies.
5. Any new NuGet package → `THIRD_PARTY_NOTICES.md` entry (license verified; test-only packages note "not redistributed").
6. Verify: `dotnet restore --locked-mode` after lockfiles generated, `dotnet build -c Release` (0 warnings), `dotnet test -c Release`. Record exact outputs. App launch = empty host only; do not claim UI behavior.
7. Update this file, NEXT ACTION = **P00-04**. Do not commit; list changed files for owner (suggested message `chore: create solution skeleton and build policy`).

**Expected verification:** clean Release build with warnings-as-errors; all tests pass; Core dependency test passes.

## TASK BOARD (compact; detailed definitions live in guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | IN_PROGRESS (P00-01, P00-02 DONE) | G00 pending |
| P01 — Reliability kernel | P01-01…P01-05 | NOT_STARTED | G01 pending |
| P02 — Island shell | P02-01…P02-05 | NOT_STARTED | G02 pending |
| P03 — Tray/settings/startup | P03-01…P03-05 | NOT_STARTED | G03 pending |
| P04 — Core widgets/data | P04-01…P04-07 | NOT_STARTED | G04 pending |
| P05 — Customization/performance | P05-01…P05-04 | NOT_STARTED | G05 pending |
| P06 — Packaging/updates | P06-01…P06-09 | NOT_STARTED | G06 pending |
| P07 — Release validation | P07-01…P07-05 | NOT_STARTED | G07 pending |

## RECENT VERIFIED CHANGES (newest first; max 10)
- 2026-10-25 P00-02 DONE (uncommitted; owner commits): added `global.json` (SDK 10.0.401, `rollForward: latestFeature`, no prerelease); ADR 0001 supported Windows versions from official `dotnet/core` supported-os.md (updated 2026-09-28) + support policy (.NET 10 LTS EOL 2028-11-14); ADR index updated.
- 2026-10-25 P00-01 DONE (uncommitted; repo init deferred to owner): added `LICENSE` (MIT, "Dynamic Island for Windows contributors"), `THIRD_PARTY_NOTICES.md` (template, no components), `CONTRIBUTING.md`, `docs/ADR/README.md` (template + index), `.gitignore` (.NET/VS/test/packaging/runtime data); rewrote `README.md` as project README (kept agent instruction); created §3 skeleton dirs `src/`, `tests/`, `packaging/portable/`, `packaging/installer/`, `.github/workflows/` with `.gitkeep`. No product code. §3 docs `DESIGN_SYSTEM.md`, `SECURITY_PRIVACY.md`, `TEST_MATRIX.md`, `RELEASE_PROCESS.md` not created yet (owned by later tasks).
- 2026-10-25: Created implementation plan and handoff template.

## TEST EVIDENCE
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P00-01 | Windows 11 Pro 10.0.26200 | Directory listing of repo root, `docs/`, `docs/ADR/` | PASS | All baseline files present; no build artifacts |
| 2026-10-25 | P00-02 | Windows 11 Pro 25H2 26200.9711 x64 | `dotnet --info`; `dotnet --list-runtimes`; `Get-ComputerInfo`; registry `CurrentVersion` | PASS | SDK 10.0.401, WindowsDesktop 10.0.12 present |
| 2026-10-25 | P00-02 | same | `dotnet --version` in repo root with `global.json` | PASS | Output `10.0.401` |

## BLOCKERS / RISKS
- No target Windows development/testing host verified yet. Do not assert WPF UI tests passed.
- Enterprise policy may block unsigned/per-user executable; never bypass.
- Self-update/rollback architecture must validate portable read-only folder and DB schema compatibility.
- Dev host is one security patch behind: runtime 10.0.12 / SDK 10.0.401 vs latest 10.0.13 / SDK 10.0.402 (2026-10-13 security release). Owner should install SDK 10.0.402 before any release build. Agent did not install it (system-wide change).

## ARCHITECTURE DECISIONS / ADRS
- Frozen D01–D15: see guide §0. No deviations approved.
- ADR 0001 (Accepted): supported Windows versions. It clarifies guide §0 and does not deviate from it.
- Pending ADRs: concrete per-user installer technology and updater trust/provenance model when P06 begins.

## HANDOFF NOTES
- Read `AGENTS.md` first; guide §5 holds task-by-task definitions.
- Current next task: **P00-03**, no product code yet. Do not jump to UI implementation.
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
