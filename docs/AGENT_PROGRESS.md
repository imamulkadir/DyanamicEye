# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01 is done (G01 PASS, confirmed on CI run 40201274900). P02-01 is done; P02-02..P02-05 are implemented and **G02 waits for the owner's hardware matrix**. P03 is in progress with owner approval; P03-01…P03-05 are implemented (awaiting manual validation); **G03 waits for the owner's hardware checks**. P04 started with owner approval while G02/G03 stay open.
- **Active task:** G04 gate evidence done locally (100-restart smoke test, update-shaped migration tests) plus a tab-selection race fix; awaiting the owner's commit and CI. Then P05-01. G02/G03/P04 manual rows are still owed by the owner.
- **Active task status:** G04 evidence **uncommitted** (the owner commits). P04-07 + tabs are committed as `b1165d1`; its CI run 40335768082 **failed** (1 integration test, a tab-selection race), fixed in this change.
- **Last passed gate:** G01 (2026-10-25; CI run https://github.com/imamulkadir/DyanamicEye/actions/runs/40201274900). G00: CI run 40196393380.
- **Repository:** https://github.com/imamulkadir/DyanamicEye (public; default branch `master`).
- **Commits:** **the owner reviews and makes all commits; agents must NOT commit.** List the changed files and a suggested message instead.
- **CI:** the `gh` CLI is authenticated as imamulkadir but is not on the agent's PowerShell PATH, so use `& "C:\Program Files\GitHub CLI\gh.exe"`.
  - `run list --repo imamulkadir/DyanamicEye -L 3`
  - `run view <id> --repo imamulkadir/DyanamicEye --log-failed`
  - `run watch <id> --repo imamulkadir/DyanamicEye --exit-status`

  The CI runner (2 vCPU) is slower than the dev box, so avoid real-timer assertions and fire-and-forget IPC.
- **Host:** Windows 11 Pro 25H2 build 26200.9711, x64. SDK 10.0.401 (pinned in `global.json`); runtimes 10.0.12; 6 CPUs.
- **Supported OS:** ADR 0001. The tested target is Win11 24H2/25H2 x64. Win10 22H2 Home/Pro is NOT supported by .NET 10.
- **Build/test** (Microsoft.Testing.Platform via `global.json`):
  - `dotnet restore DynamicIsland.sln --locked-mode`
  - `dotnet build DynamicIsland.sln -c Release --no-restore` and `dotnet test --solution DynamicIsland.sln -c Release --no-build --max-parallel-test-modules 1`
  - the same two with `-c Debug`. Debug compiles the test hooks and runs the crash-injection smoke tests.

  The smoke tests start the real app and launcher on the desktop, and they assume no other Dynamic Island instance is running.
- **Last updated:** 2026-10-26 (G04 evidence + tab race fix; next: owner commit, CI, then P05-01)

## NEXT ACTION (ONE task only)
**Owner:** review and commit the G04 evidence + tab-selection fix (file list in RECENT VERIFIED CHANGES), push, and check that CI is green (CI run 40335768082 on `b1165d1` **failed** on the tab race this fixes). Note the new 100-restart smoke test adds about 2–4 minutes to the Debug CI job.
**Then agent:** if CI is green, record **G04 automated PASS** with the CI run, then start **P05-01** (guide §5 P05: theme tokens/colors, radius/size/opacity/placement, visual hierarchy; min/max safe dimensions). Real-hardware rows MD, SM, VO, NT, BK, TB (and G02/G03) stay owed by the owner.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | DONE | **G01 PASS** 2026-10-25, CI run 40201274900 (Release + Debug). See the G01 evidence below. |
| P02 — Island shell | P02-01…P02-05 | IN_PROGRESS (01 DONE; 02–05 IMPLEMENTED_AWAITING_VALIDATION) | G02 pending (needs a real multi-DPI/multi-monitor check) |
| P03 — Tray/settings/startup | P03-01…P03-05 | IN_PROGRESS (01–05 IMPLEMENTED_AWAITING_VALIDATION; started with owner approval while G02 is open) | G03 pending (owner hardware checks) |
| P04 — Core widgets/data | P04-01…P04-07 | IN_PROGRESS (01–07 IMPLEMENTED_AWAITING_VALIDATION; started with owner approval while G02/G03 are open) | **G04 automated evidence PASS locally** (2026-10-26; `TEST_MATRIX.md` G04); awaiting CI; hardware rows owed |
| P05 — Customization/performance | P05-01…P05-04 | NOT_STARTED | G05 pending |
| P06 — Packaging/updates | P06-01…P06-09 | NOT_STARTED | G06 pending |
| P07 — Release validation | P07-01…P07-05 | NOT_STARTED | G07 pending |

### G01 evidence (guide §5: "corrupt settings recover; simulated widget failure isolated; deliberate Exit never auto-relaunches; third crash does not loop; no duplicate app")
| Criterion | Proof |
|---|---|
| Corrupt settings recover | `JsonSettingsStoreTests` (12 corrupt variants, backup recovery); real app run with a corrupt file logs `ResetAfterCorruption` (P01-03, WORK_LOG) |
| Simulated widget failure isolated | `ComponentBoundaryTests.Failing_component_is_isolated_while_others_keep_running`: the faulty component ends `Failed` after 1+3 runs, the other stays `Healthy`, no shutdown is requested, and a Warning notice is published |
| Deliberate Exit never auto-relaunches | `AppSupervisorTests.Clean_exit_is_never_relaunched`; smoke `Deliberate_exit_through_the_launcher_is_never_relaunched` with the real launcher and app |
| Third crash does not loop | `AppSupervisorTests.Crash_loop_…`; Debug end-to-end smoke `Crash_loop_restarts_twice_tries_safe_mode_once_then_stops`, also green on CI run 40200547110 |
| No duplicate app | Smoke `Second_launch_exits_quietly_and_leaves_the_first_instance_running`; `SingleInstanceGuardTests` |

## RECENT VERIFIED CHANGES (newest first; max 10; older detail in `docs/WORK_LOG.md`)
- 2026-10-26 **G04 gate evidence + tab-selection fix** (uncommitted; the owner commits).
  - **Tab race (CI run 40335768082 failure):** `ClockWidgetPresenterTests` failed on CI with "Sequence contains no elements": another widget published before the clock, became the automatic selection and kept it, so the clock view was not shown. Product bug too: the island could open on whichever widget loaded first. Fix in `NotchView`: only a tab the **user** picks (click, arrows, Ctrl+Tab, `SelectWidget`) sticks; otherwise the first tab in the user's order is shown; a pick is forgotten when its widget disappears. ADR 0025 §3 amended. New test `Until_the_user_picks_a_tab_the_first_tab_in_order_is_shown` (fails on the old code: the automatic pick stayed on "system").
  - **100 restarts:** new `tests/DynamicIsland.WindowsSmokeTests/RestartSmokeTests.cs` (Debug only, isolated data folder): seeds settings (consents answered, so no first-run window) and notes/tasks, then starts and closes the real app 100 times. Each run: exit 0, notes/tasks unchanged, `settings.json` byte-identical, no `*corrupt*` or temp files. Afterwards the logs must show no Failed widget, no Error/Critical entry and no `PreviousSessionUnclean`, and the clock widget reported Healthy. 1 m 45 s locally.
  - **Update-shaped migrations:** `SqlitePersonalDataStore` has an internal constructor taking the migration list (`DefaultMigrations` is internal); ADR 0023 consequence added. New unit tests: `Update_shaped_migration_keeps_notes_and_tasks` (v1 data → rehearsal v2 adds a column and table, data kept, writable; then the current build opens it read-only) and `Failed_update_migration_rolls_back_and_keeps_data` (user_version stays 1, no partial column, a fixed release then migrates). Settings migrations were already covered (`JsonSettingsStoreTests` legacy v0 fixture, failed migration read-only).
  - `docs/TEST_MATRIX.md`: new **G04** section (G4-1…G4-5); TB1 expects the first tab in order on first open.
  - **Changed files:**
    - modified: `docs/ADR/0023-notes-and-tasks-storage.md`, `docs/ADR/0025-tabbed-expanded-island.md`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md`, `docs/WORK_LOG.md`, `src/DynamicIsland.App/NotchView.xaml.cs`, `src/DynamicIsland.Infrastructure/PersonalData/SqlitePersonalDataStore.cs`, `tests/DynamicIsland.IntegrationTests/WidgetPanelTests.cs`, `tests/DynamicIsland.UnitTests/PersonalData/SqlitePersonalDataStoreTests.cs`
    - new: `tests/DynamicIsland.WindowsSmokeTests/RestartSmokeTests.cs`
    - Suggested commit message: `test: G04 evidence (100 restarts, update-shaped migrations); fix first tab selection race`
- 2026-10-26 P04-07 + tabs committed by the owner as `b1165d1`; CI run 40335768082 **failure**: 1 integration test (`ClockWidgetPresenterTests.Notch_and_expanded_panel_show_the_clock_and_disabling_clears_them`, tab-selection race), unit and smoke passed. Fixed in the entry above.
- 2026-10-26 **Owner request: tabbed expanded island — IMPLEMENTED_AWAITING_VALIDATION** (uncommitted). Decisions are in **ADR 0025**; ADR 0018 §6 and ADR 0022 §5 are amended.
  - The owner asked "why everything is shown at once? wasnt the plan to show in individual tabs?". The guide only says "click-to-expand dashboard"; the stacked layout was my reading of that (ADR 0018). Asked with 3 options, the owner chose **one widget per tab**.
  - **App:**
    - `NotchView.xaml` has a `TabStrip` row (hidden for 0–1 widgets).
    - `NotchView.ShowWidgets` builds one icon `RadioButton` tab per widget with content or a failure (reused by id, AutomationId `Tab.<id>`) and shows only the selected widget's element.
    - New `SelectedWidgetId`, `SelectWidget`, `SelectAdjacentWidget` and `SelectedWidgetChanged`. The selection is kept across updates and falls back to the first tab.
    - `IslandWindow`: Ctrl+Tab / Ctrl+Shift+Tab cycle tabs.
    - `WidgetPresenter` routes "expanded" only to the selected widget.
  - **Core:** `WidgetRegistry.SetExpanded(string? visibleWidgetId)` replaces `SetExpanded(bool)`; stats now sample only while their tab is visible.
  - **Tests:**
    - `WidgetPanelTests` rewritten: tabs in order, selection, kept across updates, wrap-around, change events, no strip for one widget.
    - `VolumeAndLayoutTests`: the height follows the selected tab.
    - Unit: only the visible tab's widget is expanded.
    - The visual references did not change (the empty tab strip starts collapsed).
    - Clock and media tab renderings checked by eye.
  - **Changed files (tabs):**
    - modified: `docs/ADR/{0018-widget-registry.md, 0022-volume-widget-and-content-sized-island.md, README.md}`, `docs/TEST_MATRIX.md` (TB1–TB3), `src/DynamicIsland.App/{IslandWindow.xaml.cs, NotchView.xaml, NotchView.xaml.cs, WidgetPresenter.cs}`, `src/DynamicIsland.Core/Widgets/WidgetRegistry.cs`, `tests/DynamicIsland.IntegrationTests/{VolumeAndLayoutTests.cs, WidgetPanelTests.cs}`, `tests/DynamicIsland.UnitTests/Widgets/WidgetRegistryTests.cs`
    - new: `docs/ADR/0025-tabbed-expanded-island.md`
    - Suggested combined commit message: `feat: backup and restore (P04-07, ADR 0024); one widget per tab in the island (ADR 0025)`
- 2026-10-26 **P04-07 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0024**.
  - **Core:** new `PersonalData/DataBackup.cs` (`IDataBackupService` (guide §3), `BackupPreview`, `BackupProblem`, `BackupExportResult`).
  - **Infrastructure:** new `PersonalData/DataBackupService.cs`.
    - **Export:** a ZIP containing `manifest.json` (format, version, createdUtc, appVersion, schemas, counts, size and SHA-256 per file), `settings.json`, and `islands.db` made with the SQLite **online backup API** (busy timeout plus retry). Written to a temporary file, then moved; never overwrites.
    - **Preview** (dry run into `data\restore-staging\<id>`) refuses with a reason:
      - not a ZIP;
      - manifest problems;
      - unknown, duplicate or missing entries (e.g. `../evil.dll`);
      - an entry over 64 MB or a total over 256 MB (counted while unpacking, so a lying header cannot fool it);
      - a size or SHA-256 mismatch;
      - bad settings JSON or a database failing `quick_check`;
      - a newer settings or data schema.
    - **Schedule** → `data\restore-pending`.
    - **`ApplyPendingRestore`** at startup: current files move to `rollback-<UTC>` (newest 3 kept), then the restored files move in. On failure everything is moved back and the pending folder is kept.
    - Logs 3110–3114 carry no text or paths.
  - **App:**
    - `App.xaml.cs` applies a pending restore before settings or the database are opened.
    - `AppHost` registers `IDataBackupService`.
    - Settings → Privacy → "Back up and restore": Export backup… (save dialog), Restore from backup… (open dialog → preview summary → OK → restart through `RestartService`), and a status line.
    - `AppLog` 1060 BackupFailed. `SettingsWindowContext` gains `Backup` and `Restart`.
  - **Bugs found by the tests:**
    - (1) A non-ZIP file stayed **locked** after a refused preview, because the FileStream leaked. Fixed.
    - (2) Export intermittently failed with "database is locked": the backup step only waits with a busy handler. Fixed with `busy_timeout` and a retry; unit tests passed 4×.
  - **Tests:**
    - Unit: `PersonalData/DataBackupServiceTests.cs` (12):
      - export contents and checksums while the store is open; no overwrite;
      - full round trip with a rollback copy; a failed apply keeps the previous data;
      - tampering, a newer version and five broken-archive kinds;
      - a 65 MB ZIP bomb is refused without unpacking.
    - Smoke (Debug): `RestoreSmokeTests` (the real app applies a pending restore at startup and keeps the old database in the rollback folder).
  - **Changed files for the owner:**
    - modified: `docs/ADR/README.md`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md` (BK1–BK5), `src/DynamicIsland.App/{App.xaml.cs, AppHost.cs, AppLog.cs, SettingsWindow.xaml, SettingsWindow.xaml.cs}`, `tests/DynamicIsland.IntegrationTests/SettingsWindowTests.cs`
    - new: `docs/ADR/0024-backup-export-import.md`, `src/DynamicIsland.Core/PersonalData/DataBackup.cs`, `src/DynamicIsland.Infrastructure/PersonalData/DataBackupService.cs`, `tests/DynamicIsland.UnitTests/PersonalData/DataBackupServiceTests.cs`, `tests/DynamicIsland.WindowsSmokeTests/RestoreSmokeTests.cs`
    - Suggested commit message: `feat(data): backup export and verified restore with rollback (P04-07, ADR 0024)`
- 2026-10-26 P04-06 committed by the owner as `c2bb883`; CI run 40325111433 **success**: Release 607/607, Debug 614/614, 0 skipped (covers the Debug and smoke runs owed for P04-06).
- 2026-10-26 **Owner approval:** `Microsoft.Data.Sqlite` was proposed with "Do you approve adding it?"; the reply "pushed, continue" is recorded as **approval of that dependency** (ADR 0023 §1).
- 2026-10-26 **P04-06 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0023**.
  - **Dependency:** `Microsoft.Data.Sqlite` **10.0.13** (MIT), which brings SQLitePCLRaw **2.1.13** (Apache-2.0) and native SQLite (public domain). Verified on nuget.org 2026-10-26.
    - Pinned in `Directory.Packages.props` and referenced by Infrastructure only.
    - `THIRD_PARTY_NOTICES.md` has 3 new entries.
    - Lock files updated by `dotnet restore`.
  - **Core:** new `PersonalData/PersonalData.cs` (`IPersonalDataStore`, `Note`, `TaskItem`, `PersonalDataResult`, `PersonalDataRules`: note 1–2000 / task 1–300 characters, no control characters except line breaks in notes, at most 1000 notes and 2000 tasks).
  - **Infrastructure:** new `PersonalData/SqlitePersonalDataStore.cs`.
    - Serialized and off-thread; every write is one transaction; limits are checked inside the transaction.
    - `user_version` migrations; WAL; `quick_check`. A corrupt file becomes `islands.corrupt-<UTC>.db` (newest 5 kept) and a fresh database is created.
    - A newer schema opens read-only without being modified.
    - Logs 3100–3102 carry no text.
  - **Widgets:** new `NotesAndTasksWidgets.cs`.
    - `PersonalDataWidget` base with commands `add:`, `edit:<id>:`, `toggle:<id>` and `delete:<id>`, validated.
    - `TasksWidget` shows 6, open first. `NotesWidget` shows the latest 3.
    - Snapshots carry `LastResult` for refusal messages.
  - **App:**
    - New `PersonalDataViews.cs` (add field with a placeholder behind it, delete button, inline editor, messages), `TasksView` and `NotesView`. Rows are reused by id, focus is kept when a task moves, and an item being edited is never overwritten.
    - `NotchView` widget panel inside a `ScrollViewer` (scrolls beyond 360 DIP).
    - `IslandWindow`: Escape moved to bubbling `KeyDown`, so a text box clears or cancels first.
    - The `MediaView.IconButtonStyle` template now honours content alignment and `Padding` (defaults centred), so note rows align left.
    - `AppHost` registers the store at `data\islands.db`.
  - **Tests:**
    - Unit: `PersonalData/SqlitePersonalDataStoreTests.cs` (12: CRUD, ordering, validation, limit inside the transaction, restart persistence, corrupt quarantine, newer schema read-only, SQLite ≥ 3.45). `Widgets/NotesAndTasksWidgetTests.cs` (11).
    - Integration: `NotesAndTasksViewTests` (3, real store, registry and views in an offscreen window: Enter adds, check box completes with the row reused, F2 edits, Esc clears without bubbling, too-long text shows a message and saves nothing).
    - Renderings checked by eye.
  - **Changed files for the owner:**
    - modified: `Directory.Packages.props`, `THIRD_PARTY_NOTICES.md`, `docs/ADR/README.md`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md` (NT1–NT6), `docs/WORK_LOG.md` (older entries archived), `src/DynamicIsland.App/{AppHost.cs, IslandWindow.xaml.cs, MediaView.cs, NotchView.xaml, WidgetPresenter.cs, packages.lock.json}`, `src/DynamicIsland.Infrastructure/{DynamicIsland.Infrastructure.csproj, packages.lock.json}`, `src/DynamicIsland.Launcher/packages.lock.json`, `src/DynamicIsland.Widgets/BuiltInWidgets.cs`, `tests/DynamicIsland.IntegrationTests/{ClockWidgetPresenterTests.cs, packages.lock.json}`, `tests/DynamicIsland.UnitTests/packages.lock.json`, `tests/DynamicIsland.WindowsSmokeTests/packages.lock.json`
    - new: `docs/ADR/0023-notes-and-tasks-storage.md`, `src/DynamicIsland.App/{NotesView.cs, PersonalDataViews.cs, TasksView.cs}`, `src/DynamicIsland.Core/PersonalData/PersonalData.cs`, `src/DynamicIsland.Infrastructure/PersonalData/SqlitePersonalDataStore.cs`, `src/DynamicIsland.Widgets/NotesAndTasksWidgets.cs`, `tests/DynamicIsland.IntegrationTests/NotesAndTasksViewTests.cs`, `tests/DynamicIsland.UnitTests/PersonalData/SqlitePersonalDataStoreTests.cs`, `tests/DynamicIsland.UnitTests/Widgets/NotesAndTasksWidgetTests.cs`
    - Suggested commit message: `feat(widgets): SQLite notes and tasks with transactional store (P04-06, ADR 0023)`
- 2026-10-26 P04-05 committed by the owner as `6ab223e`; CI run 40313608497 **success**: Release 582/582, Debug 589/589, 0 skipped.
- 2026-10-26 **P04-05 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0022**.
  - **Core:**
    - New `Audio/AudioContracts.cs` (`IAudioService`, `AudioState`).
    - `IslandLayout`: `Expanded` = 420×**180 minimum**; `ExpandedMaxHeight` 360; `Max` = 420×360; `ExpandedHeightFor`.
  - **Platform.Windows:**
    - New `Audio/CoreAudioInterop.cs`: classic `ComImport` declarations for MMDeviceEnumerator, IMMDevice, IMMNotificationClient, IAudioEndpointVolume and its callback, in exact vtable order.
    - New `Audio/CoreAudioService.cs`: default render endpoint; endpoint and device callbacks re-bind off the callback thread; every COM failure means no device or not changed.
  - **Widgets:** new `VolumeWidget` + `VolumeSnapshot`, event-driven, with commands `mute-toggle` and `set:N` (validated 0–100).
  - **App:**
    - **Persistent views:** new `WidgetView` base; `ClockView`; `MediaView` and `MetricsView` rewritten as persistent views; new `VolumeView` (mute toggle, slider, %; sends at most every 50 ms; ignores echoes for 400 ms after a user change).
    - `WidgetPresenter.ViewFactories`/`ViewFor` (per-widget cache) replace `Views`.
    - `NotchView.ShowWidgets` re-parents only when the visible set or order changes and caches "unavailable" notes; adds `ContentChanged` and `MeasureExpandedHeight` (UpdateLayout, then an unbounded measure).
    - `IslandWindow` expands to the measured content height (clamped 180–360 × scale) and follows content changes without moving focus.
  - **Bugs found while testing:**
    - The first measure returned a stale size; WPF skips re-measuring clean ancestors, so the measure now runs a layout pass first.
    - Parallel integration test classes hit a WPF XAML-parsing race (`XamlParseException ... 'RowDefinitions'`). New `tests/DynamicIsland.IntegrationTests/AssemblyInfo.cs` runs that assembly as one collection.
  - **Tests:**
    - Unit: `Widgets/VolumeWidgetTests.cs` (state/external change, no device, unavailable, commands valid and invalid, mute, height clamp).
    - Integration: `VolumeAndLayoutTests`:
      - real Core Audio, read-only (writes the same level back);
      - the slider sends values and is not overwritten while adjusting, through the same element instance;
      - no device;
      - the height grows with the widget count.
    - Media, metrics and clock presenter tests adapted to `ViewFor`.
    - The expanded smoke test now checks the width plus a 180–360 height range.
    - Visual references notch-expanded/pinned **regenerated** at the 180 px minimum.
    - A 4-widget rendering was checked by eye (measured 284 px).
    - Real Release app: widget-volume Healthy; exit 0.
  - **Changed files for the owner:**
    - modified: `docs/ADR/README.md`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md` (VO1–VO5), `src/DynamicIsland.App/{AppHost.cs, IslandWindow.xaml.cs, MediaView.cs, MetricsView.cs, NotchView.xaml.cs, WidgetPresenter.cs}`, `src/DynamicIsland.Core/Shell/IslandStates.cs`, `src/DynamicIsland.Widgets/BuiltInWidgets.cs`, `tests/DynamicIsland.IntegrationTests/{ClockWidgetPresenterTests.cs, MediaWidgetIntegrationTests.cs, MetricsIntegrationTests.cs, References/notch-expanded.png, References/notch-pinned.png}`, `tests/DynamicIsland.WindowsSmokeTests/IslandInteractionSmokeTests.cs`
    - new: `docs/ADR/0022-volume-widget-and-content-sized-island.md`, `src/DynamicIsland.App/{ClockView.cs, VolumeView.cs, WidgetView.cs}`, `src/DynamicIsland.Core/Audio/AudioContracts.cs`, `src/DynamicIsland.Platform.Windows/Audio/{CoreAudioInterop.cs, CoreAudioService.cs}`, `src/DynamicIsland.Widgets/VolumeWidget.cs`, `tests/DynamicIsland.IntegrationTests/{AssemblyInfo.cs, VolumeAndLayoutTests.cs}`, `tests/DynamicIsland.UnitTests/Widgets/VolumeWidgetTests.cs`
    - Suggested commit message: `feat(widgets): volume widget, persistent widget views and content-sized island (P04-05, ADR 0022)`

## TEST EVIDENCE (current task; older rows in `docs/WORK_LOG.md`)
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P03-01 | GitHub Actions `windows-2025` | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40208475923 |
| 2026-10-25 | P03-02 | Windows 11 Pro 25H2 x64, 1 monitor at 100 % | `dotnet build` Release + Debug | PASS | 0 warnings (earlier attempts failed on IDE0005; fixed). One Debug build hit a transient "temporary files folder is full / insufficient resources" MSBuild error: disk 27 GB free, RAM 7.9 GB free, no leaks found; the rebuild passed. |
| 2026-10-25 | P03-02 | same | Unit tests | PASS | 322/322 |
| 2026-10-25 | P03-02 | same | Smoke project alone: Debug 18 tests ×4, Release 14 ×4 | PASS (some skipped) | All tray actions pass, including restart 3/4 and Safe Mode. The fullscreen test was **skipped** in some runs (Windows refused foreground). |
| 2026-10-26 | G04 + tab fix | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS | Release 624/624 ×2, Debug 633/633 ×2, **0 skipped** (Debug includes the 100-restart smoke test) |
| 2026-10-26 | G04 | same | `RestartSmokeTests` alone, Debug | PASS | 100 restarts in 1 m 45 s |
| 2026-10-26 | P04-07 + tabs | GitHub Actions `windows-2025` (`b1165d1`) | CI workflow | **FAIL** | https://github.com/imamulkadir/DyanamicEye/actions/runs/40335768082: `ClockWidgetPresenterTests` (tab-selection race); fixed above |
| 2026-10-26 | tabs | Windows 11 Pro 25H2 x64, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS | Release 621/621 ×2, Debug 629/629 ×2, **0 skipped** |
| 2026-10-26 | P04-07 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS (1 skipped) | Debug 627/627 ×2. Release 619/619 once, and 618 + **1 skipped** once (the fullscreen smoke test: Windows refused the helper window foreground; a skip, not a pass). |
| 2026-10-26 | P04-06 | GitHub Actions `windows-2025` (`c2bb883`) | CI workflow (Release + Debug, including smoke) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40325111433; 607/607 and 614/614, 0 skipped. Closes the "NOT RUN" row below. |
| 2026-10-26 | P04-06 | Windows 11 Pro 25H2 x64, `CI=true` | `dotnet restore`; `dotnet build -c Release` (0 warnings); Release unit and integration ×2 | PASS (partial) | Unit 507/507 ×2, integration 85/85 ×2, 0 skipped |
| 2026-10-26 | P04-06 | same | Debug build and the smoke suite | **NOT RUN** | The owner's Debug DynamicIsland (PID 13416, started 17:13) was running: it locks `bin\Debug` and the single-instance lock the smoke tests need. It was not closed by the agent. Run when no instance is running; CI covers both. |
| 2026-10-26 | P04-05 | GitHub Actions `windows-2025` (`6ab223e`) | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40313608497; 582/582 and 589/589, 0 skipped |
| 2026-10-26 | P04-05 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2; integration alone ×2 | PASS | Release 582/582 ×2, Debug 589/589 ×2, **0 skipped**; integration 82/82 ×2 |
| 2026-10-26 | P04-05 | same, real Release app 5 s | `DynamicIsland.exe`, then close | PASS | widget-volume Healthy (Core Audio connected); exit 0 |
| 2026-10-26 | P04-04 | GitHub Actions `windows-2025` (`5f13f25`) | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40310973120; 562/562 and 569/569, 0 skipped |
| 2026-10-26 | P04-04 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2; unit tests alone ×3 | PASS (1 skipped) | Debug 569/569 ×2. Release 562/562 once, and 561 + **1 skipped** once (the fullscreen smoke test: Windows refused the helper window foreground; a skip, not a pass). Unit 469/469 ×3. |
| 2026-10-26 | P04-03 | GitHub Actions `windows-2025` (`179cef8`) | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40309259229; 545/545 and 552/552, 0 skipped |
| 2026-10-26 | P04-03 fixes | Windows 11 Pro 25H2 x64, `CI=true` | Full suite after the art-cap/retry/view fixes, Release ×1 and Debug ×1 | PASS | Release 545/545, Debug 552/552, 0 skipped |
| 2026-10-26 | P04-03 | real Media Player, owner's mp3 | Probes via `WindowsMediaSessionService` + `MediaView` rendering | FAIL → fixed | Art missing (2.36 MB BMP over the cap) → fixed; Next/Previous work with a queue; transient refusals → retry added |
| 2026-10-26 | P04-03 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet restore` (new TFM), `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS | Release 543/543 ×2, Debug 550/550 ×2, **0 skipped**; the real GSMTC adapter test included |
| 2026-10-26 | P04-03 | same, real Release app 5 s | `DynamicIsland.exe` from `bin\Release\net10.0-windows10.0.19041.0`, then close | PASS | widget-media Degraded `no-media` (nothing playing); no track data logged; exit 0 |
| 2026-10-26 | P04-02 | GitHub Actions `windows-2025` (`abd5693`) | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40303146979; 534/534 and 541/541, 0 skipped |
| 2026-10-26 | P04-02 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS (2 skipped) | Debug 541/541 ×2. Release 533 passed + **1 skipped** ×2: the fullscreen smoke test, because Windows refused the helper window foreground; that is a skip, not a pass. Run alone afterwards it **passed 2/2** (`--filter-class FullscreenSmokeTests`). The desktop was in interactive use at the time; another DynamicIsland Debug process (not started by the tests) briefly locked the build output. |
| 2026-10-26 | P04-01 | GitHub Actions `windows-2025` (`294a8f0`) | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40301030072; 533/533 and 540/540, 0 skipped |
| 2026-10-26 | P04-01 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2; unit tests alone ×3 (the registry tests are timing-based) | PASS | Release 533/533 ×2, Debug 540/540 ×2, **0 skipped**; unit 441/441 ×3 |
| 2026-10-26 | P03-05 | GitHub Actions `windows-2025` (`2dc4315`) | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40299277622; 523/523 and 530/530, 0 skipped |
| 2026-10-26 | P03-05 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS | Release 523/523 ×2, Debug 530/530 ×2, **0 skipped** |
| 2026-10-26 | P03-04 + clock | GitHub Actions `windows-2025` (`8917a21`) | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40296571628; 488/488 and 494/494, 0 skipped |
| 2026-10-26 | notch clock | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS | Release 488/488 ×2, Debug 494/494 ×2, **0 skipped** (including the idle-CPU budget and the regenerated visual references). A real Release app on the desktop showed "2:54 PM" and exited 0. |
| 2026-10-26 | P03-04 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS | Release 465/465 ×2 and Debug 471/471 ×2, **0 skipped**. Real HKCU Run key, test keys and real settings checked untouched afterwards. Offscreen renderings of onboarding and the General page checked by eye. |
| 2026-10-26 | P03-03 | GitHub Actions `windows-2025` (`c7bbbe4`) | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40290363463; 421/421 and 425/425, 0 skipped |
| 2026-10-26 | P03-03 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then `dotnet test --solution DynamicIsland.sln -c <cfg> --no-build --max-parallel-test-modules 1`: Release ×2, Debug ×2 | PASS | 0 failed in all runs. Release 421/421 once, and 420 + 1 skipped once (the fullscreen smoke test: Windows refused the helper window foreground, so a skip, not a pass). Debug 425/425 twice. Offscreen renderings of all 8 pages were checked by eye: layout, error message, banner. |
| 2026-10-26 | P03-02 | GitHub Actions `windows-2025` (`ba52b9e`) | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40286979868; Release 380/380, Debug 384/384, **0 skipped** (fullscreen and idle-CPU smoke tests both ran and passed) |
| 2026-10-26 | P03-02 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug, then `dotnet test --solution DynamicIsland.sln -c <cfg> --no-build --max-parallel-test-modules 1`: Release ×4, Debug ×3 | PASS | 0 failed in all 7 runs. Release: 380/380 three times and 379 + 1 skipped once. Debug: 384/384 twice and 383 + 1 skipped once. Both skips were the fullscreen smoke test, skipped because Windows refused the helper window foreground; this is a skip, not a pass. The intermittent failures from 2026-10-25 did not reproduce. |
| 2026-10-25 | P03-02 | same, `CI=true` | Full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | **2 intermittent FAILs** | Release run 1: fullscreen smoke failed. Debug run 1: idle CPU over 300 ms. Runs 2: Release 380 (1 skipped), Debug 384 green. Not yet diagnosed; see NEXT ACTION. |
| 2026-10-25 | P02-05 | GitHub Actions `windows-2025` | CI workflow (Release + Debug, sequential modules) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40207131071; 342/343, 0 skipped |
| 2026-10-25 | P03-01 | Windows 11 Pro 25H2 x64, 1 monitor at 100 % | Integration 44/44 and smoke 12/12 (Release), first run | PASS | Tray registered, recovered after a simulated `TaskbarCreated`, removed on exit; 0 skipped |
| 2026-10-25 | P03-01 | same, real desktop | Opened the tray menu via the callback message (no synthetic mouse) and took a screenshot; then `taskkill` | FAIL → fixed | The menu shows "Hide island" and "Exit" with focus on the first item. `taskkill` left the app running with the tray window destroyed. Fixed: WM_CLOSE on the tray window means Exit; `taskkill` now exits with code 0. |
| 2026-10-25 | P03-01 | same, `CI=true` | `dotnet test --solution … --max-parallel-test-modules 1`, Release and Debug ×2 | PASS | Release 350/350, Debug 351/351, 0 skipped, no stray processes. One earlier Debug run failed the click-through test (first-paint race); fixed in the test. |
| — | P03-01 | real Explorer restart, keyboard, high DPI | `TEST_MATRIX.md` rows Y1–Y7 | PENDING_WINDOWS | Owner |
| 2026-10-25 | P02-04 | GitHub Actions `windows-2025` | CI workflow | **FAIL** | https://github.com/imamulkadir/DyanamicEye/actions/runs/40205375218: `Island_hides_while_a_fullscreen_window_is_in_front_and_returns_afterwards`, 329/330 |
| 2026-10-25 | P02-03 notch | GitHub Actions `windows-2025` | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40204446036 |
| 2026-10-25 | P02-02 | GitHub Actions `windows-2025` | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40202761834 |
| 2026-10-25 | P02-01 | GitHub Actions `windows-2025` | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40201946771 |
| — | P02-01 | multi-monitor, mixed DPI, 125–200 % | Placement on a secondary or high-DPI monitor, fallback when unplugged | PENDING_WINDOWS | Single 100 % monitor on the dev host; covered by unit tests; G02 matrix in P02-02 || 2026-10-25 | P01-04 | GitHub Actions `windows-2025`, owner push | CI workflow (Release + Debug) | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40200547110 |

## BLOCKERS / RISKS
- Enterprise policy may block an unsigned per-user executable; never bypass it.
- Self-update and rollback must handle a read-only portable folder and DB schema compatibility (P06).
- The dev host is one security patch behind (runtime 10.0.12 / SDK 10.0.401 vs 10.0.13 / 10.0.402). The owner should update before any release build.
- Packaging (P06) must ship `DynamicIsland.Launcher.exe` next to `DynamicIsland.exe`, and shortcuts and startup target the launcher (ADR 0006).
- G02 requires multi-monitor, DPI 100–200 %, unplug/rotate and sleep/wake checks on real hardware. The dev host has one monitor visible to the agent, so the owner may need to run part of that matrix.

## ARCHITECTURE DECISIONS / ADRS
- Frozen D01–D15: see guide §0. No deviations approved.
- 0001 supported Windows versions · 0002 solution/build/test platform · 0003 settings persistence (consent defaults `NotAsked`; schema-bump rule for new enum values) · 0004 local logging and redaction · 0005 lifecycle, exit codes 0/1/3, single instance with acknowledged pipe · 0006 crash supervision, Safe Mode, Debug-only test hooks, launcher as the entry point · 0007 component error boundaries, notices (keys only), user-reviewed diagnostic export · 0008 island window (tool window + PerMonitorV2), monitor identity (device interface path, never logged), placement calculator, display-change debounce · 0009 state machine (exhaustive table), fixed-size window with animated island, motion preference, keyboard/focus return, UI Automation patterns; amended: notch design (flush top, shoulders, sensor housing) · 0010 fullscreen detection (WinEvent hooks, covers-monitor rule, per-monitor policy, exe-name exceptions, honest exclusive-fullscreen limit; amended: uncloak hook + one-shot follow-up) · 0011 idle activity budget, power-aware motion policy, offscreen visual regression with own references, sequential test modules · 0012 Win32 tray icon (v4 events, TaskbarCreated, DPI icon, WM_CLOSE → Exit).
- Pending: per-user installer technology and updater trust model (P06).

## HANDOFF NOTES
- Read `AGENTS.md` first; guide §5 has the task definitions. Current next task: **finish P03-02 per NEXT ACTION** (G02 still owed by the owner).
- Widgets and providers (P04) must be `IIsolatedComponent`s registered as optional (ADR 0007). They must honor Safe Mode (ADR 0006) and never log free text (ADR 0004).
- If code is present that this file does not describe, inspect git and tests and reconcile this file before editing.
