# 0036 — Data safety around updates: pre-update snapshot, failed migrations, rollback against newer data

- Status: Accepted
- Date: 2026-10-28
- Task: P06-07

## Context
Guide P06-07 asks for:
- a pre-update snapshot;
- schema compatibility checks and transactions;
- graceful recovery from failed migrations;
- never blindly running a rolled-back binary against an incompatible database;
- selective restore only after explicit validation.

What already existed:
- **Settings (ADR 0003):**
  - versioned JSON;
  - a newer schema, or a failed migration, loads defaults and disables saving, so the file is never overwritten.
- **Notes and tasks (ADR 0023):**
  - SQLite with `PRAGMA user_version`;
  - each migration step runs in one transaction;
  - a newer schema opens read-only;
  - a corrupt file is quarantined.
- **Backups (ADR 0024):**
  - a validated restore (`quick_check`, schema range, counts) is staged in `restore-pending`;
  - it is applied at the next start, before any store opens;
  - the replaced files move to `rollback-<UTC>`.
- **Updater (ADR 0035):** a new version that fails its health check within 60 s is rolled back. The new version may
  already have migrated the data in those 60 s, and the old version would then be stuck read-only.

## Decision
1. **Pre-update snapshot, taken by the updater.**
   - When: after the app and launcher have exited and the package hash has been re-checked, before anything in the
     layout changes.
   - What: `data\` files (`settings.json`, `settings.json.bak`, `islands.db`, `islands.db-wal`) are copied into
     `data\pre-update-<UTC>\`, together with a `snapshot.json` marker (`from`, `to`, `state`).
   - Why a plain file copy is safe: the app is closed, so the database and its WAL are consistent together. The
     updater (NativeAOT) needs no SQLite.
   - If the copy fails (for example, a locked file), the update aborts with `snapshot-failed` and nothing changes.
   - The newest two snapshots are kept.
2. **Marker states.**
   - The updater sets `Taken`, then `Committed` or `RolledBack`.
   - At its next start, the old version sets `Restored`, `Kept` or `Invalid` on a `RolledBack` snapshot. Each snapshot
     is checked once.
3. **Recovery after a rollback** (`PreUpdateRecovery`). It runs at startup right after the pending-restore step and
   before either store opens.
   - **Compatible data:** if neither the live settings nor the live database has a schema newer than this version
     supports, nothing is touched (`Kept`). Changes made by the failed version stay.
   - **Newer data:**
     1. The snapshot is validated exactly like a backup restore: SQLite online backup into staging (this folds the
        WAL in), then `quick_check` and a schema between 1 and the current version; settings must have a schema in
        range.
     2. Only then is the snapshot moved into `restore-pending` and applied through the ADR 0024 path. The upgraded
        files are kept in `rollback-<UTC>`, so nothing is deleted (`Restored`).
   - **Failed validation, a user restore still pending, or no database in the snapshot:** nothing is touched
     (`Invalid`). The stores stay read-only for the newer data, as before.
4. **Failed database migration.** A migration step that throws (other than corruption) has already rolled back its
   transaction, so the data is exactly as before. The store now logs `PersonalDataMigrationFailed` and opens
   **read-only** at the old schema. Previously it threw on every call. Settings already behaved this way.
5. **Never downgrade in place.** No code path rewrites a newer schema into an older one. Going back is always a whole
   validated copy, and the replaced files are kept.

## Consequences
- Notes edited during the at most 60 s a failed version ran are not in the restored data; they remain in
  `rollback-<UTC>\islands.db`. This is documented in the manual test UP10.
- Disk use per update: one copy of the user's data (normally well under 1 MB), with at most two copies kept.
- Event IDs:
  - 3009: snapshot mark failed (updater)
  - 3103: migration failed
  - 3120: data checked after a rollback
  - 3121: staging failed

  Logs carry states and error types only, never note or task text.
