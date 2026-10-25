# AGENTS.md — Dynamic Island Agent Operating Rules

**Read on every new session. This file is the short operational contract.**

## Sources of truth
1. `docs/AGENT_PROGRESS.md`: current state, blockers, and **exact next action**.
2. `docs/IMPLEMENTATION_GUIDE.md`: frozen product requirements and ordered task definitions.
3. Actual code/tests/git history: verify claims; code + test evidence takes precedence over unchecked progress text.

## Startup routine (minimal context)
1. Read this file and the heading/current status/`NEXT ACTION` of `docs/AGENT_PROGRESS.md`.
2. Execute `git status --short`, `git log -1 --oneline`, inspect only files mentioned by active task.
3. Read only the relevant `Pxx-yy` subsection of guide, not the whole document.
4. State task, scope, expected test; implement only the next task.
5. Run tests. Update progress even if blocked, with exact host OS and evidence.

## Non-negotiables
- **C# WPF .NET 10 LTS**, native Windows API adapters, standard-user operation, offline-first, free core, no paid APIs/servers/accounts.
- Portable EXE **and** per-user installer; user opts into Windows startup; native tray and settings.
- GitHub daily update checks and **separate user approvals** for download and installation; verify package trust, rollback, preserve data.
- Limited crash restart, Safe Mode, local redacted logs, manually reviewed GitHub bug reports.
- No external plugins in v1, no activity tracking without opt-in, no auto telemetry.
- v1 core widgets only; deferred features remain backlog.
- Don't bypass corporate application controls or request elevation by default.

## Task status vocabulary
`NOT_STARTED` / `IN_PROGRESS` / `BLOCKED` / `IMPLEMENTED_AWAITING_VALIDATION` / `DONE`.
Only use `DONE` with automated + applicable **real Windows** verification. A Linux/container build is not proof of Windows UI functionality.

## Token and work discipline
- One task ID per iteration; no giant unsolicited changes or speculative scope expansion.
- Reuse existing code; `rg` targeted files; don't dump entire repository or repeat this guide in chat.
- Avoid nonessential web research and dependency churn. Verify current official docs if an API, license, signing, or security behavior matters.
- Keep output compact: changed files, tested commands/results, next task, blockers.
- **At context limits or before stopping, update progress file first.**

## Required progress update after each task
Update `docs/AGENT_PROGRESS.md` with: active task/status; file changes; exact tests, OS and results; remaining issue; latest commit hash (if committed); one clear `NEXT ACTION`; blockers/ADRs. Keep recent history compact; append archival details to `docs/WORK_LOG.md` if needed rather than growing startup context endlessly.

## Evidence and safety
- No claims of successful compilation/testing without actual tool output.
- On unexpected failure, preserve error details, retry only after diagnosis; do not mask errors with broad catches.
- Never place auth tokens, window titles, note contents, clipboard, personal files or credentials in logs/issue drafts.
- User-facing behavior should be keyboard accessible, DPI aware and nonintrusive.
- If the current environment cannot run Windows, implement unit-tested portable portions, mark UI gate `IMPLEMENTED_AWAITING_VALIDATION`, and specify precise Windows test steps.

## Starting point
Initial repo has **no implementation**. Begin `P00-01` in the guide, then follow task order and gates.
