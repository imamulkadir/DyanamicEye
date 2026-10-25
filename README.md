# Dynamic Island for Windows

A free, open-source, offline-first **Dynamic Island** for Windows: a compact floating pill at the top of the screen that previews on hover and expands on click into a small dashboard (clock, media, system stats, volume, notes, tasks).

Built with C# / WPF on .NET 10 LTS. Runs as a standard user. No accounts, servers, paid APIs or telemetry.

> **Status:** Pre-alpha. Repository baseline only. **No application code exists yet and nothing has been built or tested.** See [`docs/AGENT_PROGRESS.md`](docs/AGENT_PROGRESS.md) for current state.

## Planned v1 scope
- Island shell: collapsed, peek, expanded and pinned states; multi-monitor selection; auto-hide for fullscreen apps.
- Widgets: clock/date, media controls, CPU/RAM/battery/uptime, master volume, local notes, local tasks.
- System tray menu, settings window, opt-in start with Windows, Safe Mode, local logs.
- Portable EXE and per-user installer. Daily GitHub update checks that ask before download and before install, with rollback.

Full requirements: [`docs/IMPLEMENTATION_GUIDE.md`](docs/IMPLEMENTATION_GUIDE.md).

## Repository layout
| Path | Purpose |
|---|---|
| `AGENTS.md` | Mandatory operating rules for AI coding agents |
| `docs/IMPLEMENTATION_GUIDE.md` | Approved specification, architecture and task order |
| `docs/AGENT_PROGRESS.md` | Persistent checkpoint: status, evidence, next action |
| `docs/ADR/` | Architecture decision records |
| `src/`, `tests/` | Solution projects (created in P00-03) |
| `packaging/` | Portable and installer packaging (P06) |

## Working with an AI agent
Give the agent this instruction (also after any context reset):

> Read `AGENTS.md` and `docs/AGENT_PROGRESS.md` first. Treat `docs/IMPLEMENTATION_GUIDE.md` as the approved specification. Execute only the task listed in `NEXT ACTION`. After each task, test, update the progress file with evidence and the next task, and commit the progress update alongside code. Never call Windows behavior verified unless tested on Windows.

`DONE` means tested, not merely written.

## Contributing and license
See [`CONTRIBUTING.md`](CONTRIBUTING.md). Original code is MIT licensed ([`LICENSE`](LICENSE)); third-party components are listed in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
