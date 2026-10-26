# 0023 — Notes and tasks: SQLite storage and widgets

- Status: Accepted
- Date: 2026-10-26
- Task: P04-06

## Context
- Guide P04-06: SQLite notes and tasks — CRUD, edits, completion, timestamps, validation; transactional changes;
  search optional; no network. Guide §4.3: data under `%LOCALAPPDATA%\DynamicIsland\`, never next to the EXE.
  AGENTS.md: note contents are never logged or put in issue drafts; avoid dependency churn.
- The owner approved adding `Microsoft.Data.Sqlite` (asked 2026-10-26; answer "pushed, continue").

## Decision
1. **Dependency:** `Microsoft.Data.Sqlite` **10.0.13** (MIT, Microsoft; aligned with the other Microsoft packages at
   10.0.13; 10.0.14 was one day old). It brings `SQLitePCLRaw.bundle_e_sqlite3` / `SQLitePCLRaw.core` **2.1.13**
   (Apache-2.0) — the version Microsoft's package declares, not the newer 3.x line — and the native SQLite library
   (public domain). Referenced only by `Infrastructure`. Listed in `THIRD_PARTY_NOTICES.md`; the Apache-2.0 full text
   ships with the installer (P06). Verified on nuget.org on 2026-10-26.
2. **Database:** one file `data\islands.db` in the app data folder. Connection settings: WAL journal, foreign keys
   on, busy timeout 3 s, `Pooling` on. Schema version in `PRAGMA user_version`, migrated forward in one transaction per
   step; a file from a newer schema is opened **read-only** (like settings, ADR 0003): lists show, edits are refused.
3. **Schema v1:** `notes(id INTEGER PRIMARY KEY, text TEXT NOT NULL, created_utc TEXT, updated_utc TEXT)` and
   `tasks(id INTEGER PRIMARY KEY, text TEXT NOT NULL, done INTEGER NOT NULL, created_utc TEXT, updated_utc TEXT,
   completed_utc TEXT NULL)`; times are ISO-8601 UTC from the injected `TimeProvider`. Search is not implemented
   (optional in the guide; the lists are short).
4. **Validation (Core, `PersonalDataRules`):** text is trimmed and must be 1–2 000 characters for a note and 1–300
   for a task, without control characters other than line breaks in notes; at most 1 000 notes and 2 000 tasks.
   Every write is one transaction; a refused write changes nothing.
5. **Corruption:** opening runs `PRAGMA quick_check`. A corrupt or non-database file is moved aside to
   `islands.corrupt-<UTC>.db` (newest 5 kept, never deleted on first detection) and a fresh database is created;
   the widget shows its "isn't available" note only if even that fails. Backups and export/import are P04-07.
6. **Threading:** all database work runs off the UI thread in a serialized store (`SqlitePersonalDataStore`), so views
   never block; widgets publish immutable snapshots.
7. **Widgets:** `TasksWidget` (open tasks first, then recently completed; at most 6 shown plus a count) and
   `NotesWidget` (latest 3 notes). Commands carry the text in memory only (`add`, `edit`, `toggle`, `delete`); logs
   record command names and failure types, never text or ids.
8. **Views:** a text box ("Add a task" / "Add a note", Enter adds, Esc clears), task check boxes (Space toggles),
   a delete button per item, and inline editing (F2 or double-click; Enter saves, Esc cancels). The expanded panel
   scrolls when the content is taller than the 360 DIP maximum (ADR 0022).

## Alternatives considered
- JSON files for notes/tasks — rejected: no transactions, whole-file rewrites, harder migrations; the guide asks for
  SQLite.
- `System.Data.SQLite` — rejected: larger, not maintained by Microsoft, mixed-mode history.
- EF Core — rejected: a large dependency for two tables.

## Consequences
- P04-07 adds backup/export/import with checksums over this database.
- Diagnostic reports never include the database (ADR 0007 already excludes the data folder contents).
- Manual rows NT1–NT6 in `docs/TEST_MATRIX.md`.
