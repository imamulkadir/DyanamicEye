# 0004 — Local logging sink and redaction

- Status: Accepted
- Date: 2026-10-25
- Task: P01-02

## Context
Guide §2 calls for `Microsoft.Extensions.Logging` with a local rolling file sink, chosen after evaluating its license. It also requires 14-day retention, a size cap and PII scrubbing. §4.3 puts logs in `%LOCALAPPDATA%\DynamicIsland\logs\`. AGENTS.md forbids logging tokens, window titles, note contents, clipboard data, personal files or credentials.

## Decision
1. **In-house `ILoggerProvider`** (`Infrastructure/Logging`), with no third-party sink. The requirements are small: about 300 lines, fully unit-tested, and the only new package is `Microsoft.Extensions.Logging.Abstractions` (MIT, already covered in the notices).
2. **Format:** one JSON object per line (`time` in UTC, `level`, `category`, `eventId`, `event`, `message`, `exception`). Files are named `dynamicisland-yyyyMMdd[.N].log` and roll on UTC day and at 5 MiB. When a file opens, files older than **14 days** are deleted, and then the oldest files are deleted while the folder exceeds **50 MiB**. Messages are capped at 16 K characters.
3. **Non-blocking:** callers enqueue into a bounded channel (4096 entries). When it is full the oldest entries are dropped, and the drop count is written as a warning. A single background writer flushes after each batch, and disposal drains the queue within 3 s.
4. **Fails safe:** an I/O error disables the sink for the rest of the session and the app keeps running. A log file that can't be deleted (locked) is retried at the next roll.
5. **Redaction (`Core/Diagnostics/LogRedactor`)** runs on the writer thread for both the message and the exception text. It replaces:
   - the profile path, with `\` and `/` forms
   - the user, machine and domain names, matched as whole words only
   - GitHub tokens, bearer tokens and `password=` / `token=` / `api_key=` style assignments
   - email addresses

   If a pattern times out, the whole message is withheld (fail closed). Redaction can't recognise free text, so **callers must never log note or task text, window titles, clipboard contents or file names chosen by the user.** Code review enforces this.
6. **Host configuration:** this sink is the only provider (no console, debug or EventLog). The minimum level is Information, and `Microsoft.*` / `System.*` are raised to Warning. The host, and with it the logger, is built before the first window. `AppHost.CreateBuilder(dataRoot)` takes a data root, so tests never write to the real profile.

## Alternatives considered
- Serilog with `Serilog.Sinks.File` (Apache-2.0): capable, but it adds two packages and a configuration surface for features we don't need. Revisit if requirements grow.
- NLog (BSD-3): rejected for the same reason.

## Consequences
- Log events are defined as `[LoggerMessage]` methods, because CA1848 and CA1873 are enforced outside tests.
- Diagnostics export (P01-05) reuses `LogRedactor` and these files.
- Log level is not configurable by the user yet. Add a setting under rule 7 of ADR 0003 if that's needed.
