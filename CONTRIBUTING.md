# Contributing

Thanks for helping build Dynamic Island for Windows.

## Before you start
- Read `AGENTS.md` (operating rules) and `docs/AGENT_PROGRESS.md` (current state and next task).
- `docs/IMPLEMENTATION_GUIDE.md` is the approved specification. Frozen decisions D01–D15 change only via an ADR in `docs/ADR/`.
- Work on one task ID at a time, in guide order.

## Ground rules
- C# / WPF on .NET 10 LTS (`net10.0-windows`). No Electron, WebView, services, drivers or elevation-by-default.
- Offline-first; no telemetry, accounts or paid APIs. Network access only for user-enabled update checks.
- No external plugin loading in v1. Deferred features stay in the backlog.
- Never log or attach to bug reports: tokens, window titles, note/task text, clipboard contents, file names or credentials.
- New dependencies: pin the version and add an entry to `THIRD_PARTY_NOTICES.md` in the same change.

## Pull requests
- Keep changes small and scoped to one task.
- Include test evidence (exact commands and output summary). Windows UI behavior must be validated on real Windows; otherwise mark it `IMPLEMENTED_AWAITING_VALIDATION`.
- Update `docs/AGENT_PROGRESS.md` in the same commit as the change.
- Commit messages: Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `test:`).

## Bug reports
Use GitHub Issues. Diagnostic bundles are generated locally and must be reviewed by you before attaching; the app never uploads them automatically.

## License
By contributing you agree your contributions are licensed under the MIT License (`LICENSE`).
