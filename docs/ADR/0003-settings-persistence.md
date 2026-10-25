# 0003 — Settings persistence, recovery and versioning

- Status: Accepted
- Date: 2026-10-25
- Task: P01-01

## Context
Guide §4.3 requires these for `settings.json`: a `schemaVersion`, atomic write-to-temp-and-replace, validation, a backup when a corrupt file is loaded, and "never blindly downgrade". Guide §0 requires D02 (startup only with consent), daily update checks, and no network access other than update checking the user explicitly chose. D12 requires rollback to a previous binary without harming user data.

## Decision
1. **Model:** immutable records in Core (`AppSettings`) whose defaults are first-run values. They hold no personal content. `SettingsValidator` repairs rather than rejects: missing sections and undefined enum values fall back to defaults, numbers are clamped, and the widget list is de-duplicated, with unknown entries dropped and missing built-ins appended. Correction messages name the setting and never its value.
2. **Consent fields:** `General.StartWithWindows` and `Updates.AutomaticChecks` are `ConsentState` values that default to `NotAsked`. The first-run flow (P03) asks for both. Automatic update checks therefore stay off until the user grants them, which reconciles "daily checks" with "only explicitly chosen network access".
3. **Atomic save:** write a temp file in the same folder, flush it to disk, then `File.Replace`, keeping the previous file as `settings.json.bak`. Brief retries cover transient sharing violations (3 attempts).
4. **Corrupt load:** a file is corrupt if it is unparsable, not a JSON object, larger than 1 MiB, has an invalid `schemaVersion`, has duplicate keys or the wrong value types, or is a legacy file with no migration path. A corrupt file is moved to `settings.corrupt-<UTC>.json`, keeping the newest 5. Then `.bak` is tried, then defaults. Corrupt files are never deleted on first detection.
5. **Newer schema:** the store returns defaults and becomes read-only (`SkippedReadOnly` on save), so the file is never overwritten. This protects the user's data when a build rolls back (D12).
6. **Older schema:** the file is copied to `settings.v{N}.backup.json`, then migrated with ordered `vN→vN+1` steps on `JsonObject`, then saved. A migration that throws, or that produces unusable output, leaves the file untouched and makes the store read-only.
7. **Versioning rule:** bump `CurrentSchemaVersion`, and add a migration, for any **new enum value**, renamed or removed property, or change of meaning. Older builds reject unknown enum names, and the bump turns that into the safe read-only path rather than a reset. Adding a new property that has a default needs no bump.
8. **Serializer:** reflection-based `System.Text.Json`, not source-generated. A unit test showed the source generator assigns every init-only property, which turns missing values into `null`/`0` and loses the record defaults. Settings are tiny and loaded rarely, so the performance cost doesn't matter. The app does not trim.

## Alternatives considered
- Reject an invalid file outright instead of repairing it: rejected, because one bad value would wipe every preference.
- A lenient enum converter that maps unknown names to defaults: deferred. The schema-bump rule covers the rollback case more simply.
- Source-generated JSON with mutable `set` properties: rejected, because it gives up immutability for no real benefit.

## Consequences
- P03 must show the consent prompts and map `NotAsked` to "off".
- Anyone changing `AppSettings` must follow rule 7. Code review checks this.
- Diagnostics (P01-05) may report load outcomes and correction names, never file contents or paths containing the user name.
