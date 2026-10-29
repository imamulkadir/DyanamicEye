# Threat model: Dynamic Island for Windows (v1)

Reviewed 2026-10-29 for P07-01 (guide §4.6 and §5). Scope: the app, launcher, updater, installer, release pipeline
and repository as of commit `0284bae`, plus the fixes listed in [§6](#6-fixes-made-in-this-review). Re-review this
document whenever a trust boundary changes, for example a new network endpoint, file format or widget that reads
system data.

## 1. Assets

| Asset | Where | Why it matters |
|---|---|---|
| Notes and tasks | `%LOCALAPPDATA%\DynamicIsland\data\islands.db` | The user's personal text. It must never be lost, logged or sent anywhere. |
| Settings | `data\settings.json` (+ `.bak`) | A wrong or unreadable file must not stop the app from starting. |
| Release-signing private key | The owner's PC only (`%USERPROFILE%\DynamicIsland-release-signing-key.pem`) | Anyone with it can sign "updates" that every installed copy accepts (ADR 0034). |
| Installed program files | Portable folder or `%LOCALAPPDATA%\Programs\DynamicIsland` | They are replaced during updates, so a failed update must restore them (ADR 0035). |
| Local logs and diagnostic reports | `logs\`; reports the user saves | They may be shared in bug reports, so they must stay free of personal data (AGENTS.md). |
| The user's machine | n/a | The app runs as a standard user and never elevates, so it must not become a way to escalate privileges. |

## 2. Trust boundaries

1. **GitHub → app.** The release metadata (`api.github.com`), the signed manifest and the update package. All three
   are untrusted until the signature and hash checks pass.
2. **User-chosen files → app.** A backup archive being restored, and folders chosen for export or diagnostics.
3. **Local disk → app.** `settings.json`, `current.json`, `islands.db` and `update-request.json`. Any process running
   as the same user can change them; other programs (antivirus, backup tools) can lock them; administrators or policy
   can deny access.
4. **Windows APIs → app.** Media sessions, Core Audio, the registry (Run key, uninstall entry) and the per-user mutex
   and named pipe. These can be denied by policy or by a copy running elevated.
5. **App → updater → launcher.** Hand-over through a per-user request file and process IDs (ADR 0035).
6. **Repository and CI → release.** The GitHub Actions workflows, third-party actions and NuGet packages
   (ADR 0038).

Out of scope for v1: OAuth providers, plugins, and network access other than the opt-in GitHub update check (guide
§4.6; backlog B02 and B08).

## 3. Assumptions

- An attacker with code execution as the same Windows user can already read and change everything the user can.
  Defending against that is not a goal. The goal is that the app adds no new way to gain privileges, and that
  untrusted *content* (downloads, backups, API text) cannot take control of it.
- GitHub and its TLS are trusted for availability only. Integrity comes from the pinned key (ADR 0034).
- The owner keeps the signing key off CI and out of the repository (ADR 0038).

## 4. Threats and mitigations

Tests are in `tests/DynamicIsland.UnitTests` unless another project is named. "(new)" marks something added in this
review.

### 4.1 Update supply chain (boundaries 1, 5, 6)

| Threat | Mitigation | Evidence |
|---|---|---|
| Tampered or substituted update package | The manifest is signed with ECDSA P-256 and checked against keys pinned in the app. The size and SHA-256 of every file must match the manifest. A file that fails is deleted (ADR 0034). | `ReleaseManifestTests`; `GitHubUpdateDownloaderTests.A_manifest_signed_by_another_key_or_changed_after_signing_is_refused`, `A_file_that_does_not_match_the_signed_hash_is_deleted` |
| Malicious release metadata (URLs, HTML in notes, huge responses) | Only https download URLs on `github.com/<repo>/releases/…` are accepted: no user info, no port, no `..`. One bad asset rejects the whole release. Responses are capped at 1 MB; notes are rendered as plain text with links removed. | `SemanticVersionAndParserTests.Untrusted_release_fields_reject_the_release`, `One_untrusted_asset_rejects_the_whole_release`; `ReleaseNotesTests.Html_and_scripts_stay_inert_text`; `GitHubReleaseSourceTests.Offline_untrusted_and_oversized_answers_never_throw` |
| Zip-slip or path traversal in the update archive | Entry names are checked before staging and again during extraction. Every target is resolved to a full path and must stay under the `.partial` folder. Only allow-listed root files may be written. Limits: 20,000 entries and 2 GB. | `UpdateSwapTests.Archive_escaping_the_layout_aborts_and_leaves_nothing_behind`; `GitHubUpdateDownloaderTests.A_correctly_signed_but_unsafe_or_wrong_zip_is_rejected` (`zip-slip`) |
| Package swapped after it was checked (time-of-check to time-of-use) | The updater checks the size and SHA-256 again just before installing. | `UpdateSwapTests.Package_changed_after_download_aborts` |
| A broken update leaves the user without a working app | Health handshake with automatic rollback. Every abort after the app has exited restarts the old version (ADR 0035 amendment). Commit and rollback cleanup are best effort (new). | `UpdateSwapTests.*` (UP10 and UP11 passed on real Windows, `TEST_MATRIX.md`) |
| Downgrade or replay of an older signed release | The updater refuses a version that is not newer than `current.json`. The manifest version must match the release. | `UpdateSwapTests.Same_or_older_versions_are_rejected` |
| Forged `update-request.json` (same user) | Only the per-user request path is accepted. The package must be inside `updates\<version>\` and the version must be newer. The updater runs as the user, with no elevation manifest. | `UpdateSwapTests.Request_round_trips_and_rejects_invalid_content`, `Package_outside_the_updates_folder_is_rejected` |
| Compromised CI or third-party action | Every `uses:` is pinned to a 40-character commit SHA. Permissions are least privilege. There is no `pull_request_target` and no secrets. CI only creates **drafts**; the manifest is signed on the owner's PC after `gh attestation verify` (ADR 0038). | `ReleasePipelineTests` (now checked for every workflow, new) |
| Signing key leaked through the repository or CI | The key lives outside the repo. `New-ReleaseSigningKey.ps1` refuses to write it inside the repo. `.gitignore` covers `*.pem *.key *.pfx *.p12 *.snk .env*` (new). `New-ReleaseManifest.ps1` no longer accepts the key from an environment variable (new). | `git check-ignore` (P07-01 evidence); `ReleasePipelineTests.No_workflow_uses_secrets_or_the_signing_key` (new) |

### 4.2 User-chosen backup files (boundary 2)

| Threat | Mitigation | Evidence |
|---|---|---|
| Traversal or extra files in a backup | Only exact entry names are allowed, with no duplicates. Content is checked against the manifest's hashes while it is unpacked. | `DataBackupServiceTests.Broken_archives_are_refused_with_a_reason` (`../evil.dll`), `Tampered_content_is_refused` |
| Zip bomb | Each entry is counted as it is unpacked: 64 MB per entry, 256 MB in total. | `Oversized_entries_are_refused_without_unpacking_them` |
| Corrupt compressed data crashes the app | It is refused as `DamagedContent` (new). | `Damaged_compressed_data_is_refused_instead_of_failing` (new) |
| A database the app could not have written (bad timestamps, text that breaks the rules, `done` outside 0 or 1, over the limits) fails later in the widgets | The restore check reads every row with the app's own rules (new). | `Rows_the_app_could_not_have_written_are_refused` (new) |
| Misleading text in the restore dialog | `appVersion` is shown only if it is a short version string (new). | `Only_a_plausible_app_version_is_shown_for_a_backup` (new) |
| Data from a newer version | Refused (`NewerVersion`). | `Settings_or_data_from_a_newer_version_are_refused` |
| A restore fails halfway | The previous data is put back. Each undo step is best effort and a file that cannot be moved back stays in the rollback folder (new, event 3115). | `A_restore_that_cannot_be_applied_keeps_the_previous_data` |

### 4.3 Local files and permission denials (boundaries 3, 4)

| Threat | Mitigation | Evidence |
|---|---|---|
| Corrupt or oversized `settings.json` | 1 MB limit and JSON depth 32. The file is normalized, or quarantined and the backup or defaults used. | `JsonSettingsStoreTests.Oversized_file_is_treated_as_corrupt` and others |
| `settings.json` locked (backup tool, antivirus) or access denied: the app crashed at every start and the launcher looped into Safe Mode | Read-only defaults (`UnreadableReadOnly`). The file is left untouched and the read-only banner is shown (new). | `A_locked_settings_file_gives_read_only_defaults_and_is_left_untouched` (new) |
| A note, task or volume command fails because the store is read-only, denied or I/O fails, or the audio device is denied, and the exception reaches the UI thread and shuts the app down | The command fails and is logged by error type; the app keeps running (new). | Integration test `NotesAndTasksViewTests.A_store_that_cannot_write_loses_the_command_but_never_crashes_the_ui_thread` (new; fails without the fix) |
| A copy running elevated owns the mutex, so a normal start crashed before the crash handlers were installed | Treated as "another copy is running" (new). | Integration test `SingleInstanceGuardTests.A_running_copy_this_process_may_not_open_counts_as_the_running_instance` (new; fails without the fix) |
| Staged package locked during the updater's re-check, so the updater crashed with the app already closed | Abort and restart the old version (new, reason `package-unreadable:*`). | `UpdateSwapTests.A_package_locked_during_the_recheck_restarts_the_old_version_instead_of_crashing` (new) |
| Locked old payload or denied registry write at commit undid a healthy update | Cleanup after commit is best effort (new, event 3010). | `UpdateSwapTests.Tidying_up_after_a_healthy_update_never_undoes_it` (new) |
| A rollback step fails, so the old version never restarted | Each rollback step is best effort; the new payload is kept if the pointer cannot be restored (new). | `UpdateSwapTests.A_rollback_that_cannot_remove_the_new_version_still_restarts_the_old_one` (new) |
| Malformed updater arguments (`--request ""`) or an unreadable request crashed the updater | Treated as invalid arguments, exit code 4 (new). | Code review; the Updater has no test project. `ReadRequest` in `Updater/Program.cs` |
| Unwritable install folder | No install in place: the user gets the verified ZIP through "Show file" (ADR 0037). | `UpdateLayoutAccessTests`; UP11 (b) PASS on real Windows |
| Startup Run key blocked by policy | Reported as a startup state; nothing is written. | `StartupPolicyTests.Policy_block_and_missing_launcher_write_nothing`, `RegistryStartupRegistrationTests.Per_user_run_policy_is_reported` |
| Unwritable logs, crash history or session marker | The log sink switches off; crash history is kept in memory; the marker failure is logged. | `FileLoggerProviderTests.Unwritable_folder_disables_the_sink_without_throwing`, `CrashRecoveryTests.Unwritable_folder_keeps_history_in_memory`, `FileSessionStateMarkerTests.Unwritable_location_does_not_throw` |
| AppLocker or WDAC blocks the app | The launcher catches the start failure and says the files may be missing or blocked. | `Launcher/Program.cs` |
| WinRT media APIs denied | Errors are caught and the media widget shows nothing. Widget failures are contained by `ComponentBoundary`. | `WindowsMediaSessionService`; `ComponentBoundary` tests |

### 4.4 Privacy (boundaries 3, 4)

| Threat | Mitigation | Evidence |
|---|---|---|
| Personal text in logs | Every log call goes through `[LoggerMessage]` with fixed fields (types, counts, versions, reason codes). Media titles, window titles, notes, tasks and the clipboard are never passed. | Code review (P07-01 secrets audit) |
| File names and paths in exception text | Exceptions are logged as type, HRESULT and stack trace only, with no messages, so a backup path on another drive or a WinRT or SQLite message cannot leak (new). | `FileLoggerProviderTests.Exception_type_and_stack_are_written_without_the_message`, `Inner_and_aggregated_exceptions_are_described_by_type` (new) |
| Profile path, user, machine and domain names, tokens and emails | Removed by `LogRedactor` from messages, and again in diagnostic reports. | `LogRedactorTests` |
| Telemetry or undisclosed network use | The only endpoint is `api.github.com/repos/imamulkadir/DyanamicEye/releases/latest`, plus downloads from `github.com/<repo>/releases`. Automatic checks need opt-in (default `NotAsked`) and IT policy can disable them. The host is built with `DisableDefaults`, so there is no EventLog, console or environment-variable configuration. | `UpdateChecker`; `AppHost.CreateBuilder` |
| Bug reports leak data | The diagnostic report is redacted, previewed by the user, and saved locally only. Nothing is uploaded and no issue is filed automatically. | `DiagnosticBundleBuilderTests` |

## 5. Dependencies and licenses

Shipped packages, from the `packages.lock.json` files of the `src` projects (2026-10-29):

| Component | Version | License | Notice |
|---|---|---|---|
| Microsoft.Extensions.* (Hosting, Logging, DI, Options, Configuration, …) | 10.0.13 | MIT | `THIRD_PARTY_NOTICES.md` |
| Microsoft.Data.Sqlite, Microsoft.Data.Sqlite.Core | 10.0.13 | MIT | yes |
| SQLitePCLRaw.* | 2.1.13 | Apache-2.0 | yes |
| SQLite (`e_sqlite3`) | bundled | Public domain | yes |
| .NET runtime and WPF (self-contained), NativeAOT runtime in the launcher and updater (ILCompiler 10.0.12) | 10.0.12 | MIT | yes |
| Microsoft.Windows.SDK.NET.dll, WinRT.Runtime.dll | 10.0.19041.57 | Windows SDK license, REDIST list | yes (updated) |
| Inno Setup | 6 | Inno Setup License | yes |

- **REDIST open item: closed.** The Windows SDK license allows distributing files on its REDIST list. The official list
  (https://learn.microsoft.com/en-us/legal/windows-sdk/redist, updated 2024-10-21) has a
  "Microsoft.Windows.SDK.NET.Ref" section. It names `./lib/net8.0/Microsoft.Windows.SDK.NET.dll` and
  `./lib/net8.0//WinRT.Runtime.dll`, which "may be distributed unmodified … as part of your program in order to
  enable your application to call WinRT Apis".
  - The shipped DLL is byte-identical to the package's `lib/net8.0` copy (`cmp`, 2026-10-29).
  - Conditions from the license, recorded in the notices:
    - distribute only unmodified files;
    - add significant functionality;
    - show our own copyright notice;
    - do not use Microsoft trademarks in the program name;
    - end-user terms must protect the files at least as much as Microsoft's terms do. The notice says the file
      remains under Microsoft's terms, not MIT.
- **Unused but shipped:** `Microsoft.Extensions.Configuration.UserSecrets`, `.EnvironmentVariables`, `.CommandLine`,
  `Logging.EventLog`, `.Console` and `.Debug` come in through `Microsoft.Extensions.Hosting`. They are never
  activated, because `DisableDefaults = true`. No action for v1. Leaving them out would mean replacing the Hosting
  package, which is not worth the dependency churn.
- Test-only packages (xunit.v3, Microsoft.Testing.Platform) are not redistributed.

## 6. Fixes made in this review

All have automated tests. The UI-thread and mutex fixes were also shown to fail without the change.

1. Updater: an unreadable staged package, unreadable layout, malformed path or bad arguments no longer crash it. Commit
   cleanup never undoes a healthy update. Every rollback step is best effort. New event 3010 ("Updater step … failed; continuing").
   `UpdateSwap.cs`, `Updater/Program.cs`.
2. Backup restore: corrupt compressed data, database rows the app could not have written, and implausible
   `appVersion` text are all refused or ignored. The undo after a failed restore is best effort (event 3115).
   `DataBackupService.cs`.
3. Settings: a locked or denied `settings.json` gives read-only defaults instead of a startup crash loop.
   `SettingsLoadOutcome.UnreadableReadOnly`; `JsonSettingsStore.cs`; banner text in `SettingsWindow.xaml`.
4. Widget commands: storage, I/O and access errors are logged instead of crashing the UI thread.
   `WidgetPresenter.Execute`.
5. Single instance: a mutex this process may not open (a copy running elevated) means "already running".
   `SingleInstanceGuard.TryAcquire`.
6. Logs: exception messages are no longer written (type, HRESULT and stack only). `ExceptionText`, `FileLoggerProvider`.
7. Repository: `.gitignore` covers key and secret files; `New-ReleaseManifest.ps1 -KeyPath` is now required (the
   environment-variable path is gone); the pipeline test applies to every workflow.

## 7. Open risks (accepted for v1 or deferred)

| Risk | Severity | Decision |
|---|---|---|
| A denied or unwritable data folder is silent: no banner; onboarding returns at every start; pin and tray changes are lost (only logged, and logging is off too). | Low (no crash, no data loss) | Deferred to P07-03 (first-run UX). Show a notice when settings cannot be saved. |
| A failed restore is retried at every start, and the user is not told after "Restarting to finish the restore…". | Low | Deferred to P07-03. Show the result after a restart. |
| Download redirects are not limited by host (default HttpClient handling). | Low | Accepted: the signature and hash decide integrity. |
| Zip size limits use the size declared in the header. | Info | Accepted: only signed packages reach the updater; backups are counted while unpacking. |
| `layoutRoot` in the update request is not tied to the updater's own install folder. | Low | Accepted: only reachable from the same user; no privilege gain. |
| Media title and artist have no length limit before display. | Low | Accepted: the display trims them; they are never logged. |
| The release job reuses the shared `setup-dotnet` cache. | Low | Accepted: locked-mode restore checks package hashes. |
| A tag push alone creates a draft release, with no protected environment. | Info | Accepted: publishing needs the owner's local signing (`Complete-Release.ps1`). |
| `JsonSettingsStore` catches every exception around migration (an AGENTS.md exception). | Info | Accepted and documented in the code: a broken migration must not crash startup. It goes read-only. |
| The redactor cannot recognize free text, or names shorter than 3 characters. | Low | Mitigated by never logging free text, and by message-free exceptions (§6.6). |
