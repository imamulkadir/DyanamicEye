# 0024 — Backup, export and import

- Status: Accepted
- Date: 2026-10-26
- Task: P04-07

## Context
- Guide P04-07: backup/export/import with schemas, checksums, dry-run preview, atomic restore; corrupt archive and
  version-mismatch tests; user-selected destination. Guide §3 contract `IDataBackupService`. D12: rollback without
  harming user data. ADR 0003 (settings) and ADR 0023 (notes/tasks database).
- The database is open while the app runs (WAL), so copying the file is not a consistent backup, and replacing it
  under live connections is unsafe.

## Decision
1. **Archive:** a ZIP file the user names through a save dialog (default
   `dynamicisland-backup-<yyyyMMdd-HHmmss>.zip`), written to a temporary file next to the destination and moved into
   place only when complete; an existing file is never overwritten silently (the dialog asks; the service refuses).
   Entries, exactly:
   - `manifest.json` — `format` = `dynamicisland-backup`, `formatVersion` = 1, `createdUtc`, `appVersion`,
     `settingsSchema`, `dataSchema`, `notes` and `tasks` counts, and for each file its name, size and **SHA-256**;
   - `settings.json` — the current settings file (if one exists);
   - `islands.db` — a consistent copy made with SQLite's **online backup API** while the app keeps running.
   Nothing else (no logs, no paths); the archive holds personal data, and the UI says so.
2. **Preview (dry run):** opening an archive validates it completely before anything changes and shows created date,
   app version, note and task counts. It is refused, with a reason, when: not a ZIP; manifest missing or of another
   format/version; unknown, duplicate or missing entries; an entry larger than 64 MB or a total over 256 MB (ZIP
   bombs); a size or SHA-256 mismatch; settings that are not valid JSON; a database failing `quick_check`; or a
   settings or data schema **newer** than this build supports (older ones are migrated on load, ADR 0003/0023).
   Files are extracted to a private staging folder during the preview.
3. **Atomic restore at the next start:** approving the preview moves the staged files to `data\restore-pending\`
   (with its manifest) and restarts the app (ADR 0013 restart contract). At startup, before settings or the database
   are opened, the pending restore is applied:
   1. the current `settings.json`, `settings.json.bak`, `islands.db` (+ `-wal`/`-shm`) move to
      `data\rollback-<UTC>\` (newest 3 kept);
   2. the restored files move into place (same volume, so each move is atomic);
   3. on any failure the rollback copy is moved back and the pending folder is kept for diagnosis; the app then starts
      with the previous data and logs `RestoreFailed` (type only).
   A restore never deletes the user's previous data; it stays in the rollback folder.
4. **UI:** Settings → Privacy → "Back up and restore": "Export backup…" (save dialog) and "Restore from backup…" (open
   dialog → preview message with the counts and a warning that current notes, tasks and settings are replaced, kept as a
   rollback copy, and that the app restarts → Restore / Cancel). Results appear in the page's status line.
5. **Logs:** event names, counts, sizes and exception types only; never file paths, note or task text.

## Alternatives considered
- Copying the open database file — rejected: inconsistent under WAL.
- Swapping files while the app runs — rejected: requires closing every connection and widget mid-flight; a restart is
  simpler and safer.
- Encrypting archives — deferred: needs a password UX; the user stores the file where they choose.

## Consequences
- P06 updates use the same rollback idea for app binaries; data compatibility rules stay schema-based.
- Manual rows BK1–BK5 in `docs/TEST_MATRIX.md`.
