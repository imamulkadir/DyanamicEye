# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01 is done (G01 PASS, confirmed on CI run 40201274900). P02-01 is done; P02-02..P02-05 are implemented and **G02 waits for the owner's hardware matrix**. P03 is in progress with owner approval; P03-01…P03-05 are implemented (awaiting manual validation); **G03 waits for the owner's hardware checks**. P04 started with owner approval while G02/G03 stay open.
- **Active task:** P05-02 (remembered pin, "Try again" for failed widgets) IMPLEMENTED_AWAITING_VALIDATION, uncommitted. P05-01 committed. G02/G03/P04/P05 manual rows are still owed by the owner.
- **Active task status:** P05-02 **uncommitted**. Title removal + scroll fix committed as `d5ba82b`; CI run 40356940917 **success** (650/650, 659/659, 0 skipped).
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
- **Last updated:** 2026-10-26 (P05-01 IMPLEMENTED_AWAITING_VALIDATION; next: owner commit, CI, then P05-02)

## NEXT ACTION (ONE task only)
**Owner:** review and commit P05-02 (file list in RECENT VERIFIED CHANGES), push, and check that CI is green. PN1–PN2 in `docs/TEST_MATRIX.md` are quick manual checks.
**Then agent:** **P05-03** (guide §5 P05: keyboard-only flow, focus indicators, AutomationProperties, screen reader check, high contrast, reduced motion, minimum touch targets). Start with an audit of what exists (focus rings, UIA names, reduced motion are partly done; high contrast is open per ADR 0026 §6) and fix only the gaps.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | DONE | **G01 PASS** 2026-10-25, CI run 40201274900 (Release + Debug). See the G01 evidence below. |
| P02 — Island shell | P02-01…P02-05 | IN_PROGRESS (01 DONE; 02–05 IMPLEMENTED_AWAITING_VALIDATION) | G02 pending (needs a real multi-DPI/multi-monitor check) |
| P03 — Tray/settings/startup | P03-01…P03-05 | IN_PROGRESS (01–05 IMPLEMENTED_AWAITING_VALIDATION; started with owner approval while G02 is open) | G03 pending (owner hardware checks) |
| P04 — Core widgets/data | P04-01…P04-07 | IMPLEMENTED_AWAITING_VALIDATION (started with owner approval while G02/G03 are open) | **G04 automated PASS** 2026-10-26, CI run 40342549483 (`706daf7`); `TEST_MATRIX.md` G04; real-hardware rows MD/SM/VO/NT/BK/TB owed by the owner |
| P05 — Customization/performance | P05-01…P05-04 | IN_PROGRESS (01–02 IMPLEMENTED_AWAITING_VALIDATION) | G05 pending |
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
- 2026-10-26 **P05-02 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0027**.
  - Audit: widget order/on-off, appearance and behavior already persist and apply without recreating the island; widget settings errors already recover on load (`NormalizeWidgets`). Gaps fixed: the pin was session-only; a Failed widget needed Settings off/on or a restart.
  - **Core:** `BehaviorSettings.Pinned` (+ equality); `SettingsEditor` merge keeps the latest pin; `IslandSnapshot.IsPinned` (pinned, or hidden and returning pinned); `IIslandStateMachine.IsPinned` (default member; the real machine uses the snapshot); `ComponentHost.RetryAsync` (only enabled + Failed + not running + started + not paused + not Safe Mode); `WidgetRegistry.RetryAsync`.
  - **App:** new `PinMemory` (restores at start, saves pin/unpin in order, logs `PinNotSaved` 1061 by type); registered in `AppHost`, started in `App.xaml.cs` after the presenter. `NotchView`: failed tab note has a **Try again** button (`Retry.<id>`) raising `RetryRequested`; `WidgetPresenter.Retry`.
  - **Tests:** unit `IslandStateMachineTests.Pin_survives_hiding_but_not_unpinning_or_escape` (8 cases), `WidgetRegistryTests.Try_again_restarts_only_a_failed_enabled_widget` and `..._does_nothing_while_disabled_paused_or_in_safe_mode`, `SettingsEditorTests` merge keeps the pin; integration `PinMemoryTests` (restore, save/no save on hide and fullscreen, Escape, failed save then retry; found and fixed out-of-order saves), `WidgetPanelTests.A_failed_widgets_tab_offers_try_again`; smoke `PinSmokeTests` (real app starts pinned from settings, UIA Toggle unpins, `settings.json` saved `pinned: false`, exit 0).
  - **First full run found 2 failures (Debug, one per run):**
    - `Try_again_restarts_only_a_failed_enabled_widget`: `RetryAsync` returned false because Failed is reported just before the run loop ends (`IsRunning` still true). Product race too (a very quick click). Fix: `ComponentHost.RetryAsync` waits for the failed loop to end (`StopAsync`) and then starts it. Then 8/8 repeats green.
    - `TrayActionSmokeTests.Pause_hides_the_island_and_resume_brings_it_back`: "Timed out waiting for menu item Tray.TogglePause" (cleanup then hit a locked `islands.db-shm` because the app was still running). Not reproduced in 3 repeats. Likely cause: a popup menu closes itself when the foreground changes (the first-run window closing just before). Test-driver fix: `TrayMenuDriver` asks for the menu again if its first item has not appeared within 2 s (up to 10 s). Tray smoke 7/7 ×2 after.
  - **Changed files:**
    - modified: `docs/ADR/README.md`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md` (PN1–PN2), `docs/WORK_LOG.md`, `src/DynamicIsland.App/{App.xaml.cs, AppHost.cs, AppLog.cs, NotchView.xaml.cs, WidgetPresenter.cs}`, `src/DynamicIsland.Core/Components/ComponentHost.cs`, `src/DynamicIsland.Core/Settings/AppSettings.cs`, `src/DynamicIsland.Core/Shell/{IslandStateMachine.cs, IslandStates.cs, SettingsEditor.cs}`, `src/DynamicIsland.Core/Widgets/WidgetRegistry.cs`, `tests/DynamicIsland.IntegrationTests/WidgetPanelTests.cs`, `tests/DynamicIsland.UnitTests/Shell/{IslandStateMachineTests.cs, SettingsEditorTests.cs}`, `tests/DynamicIsland.UnitTests/Widgets/WidgetRegistryTests.cs`, `tests/DynamicIsland.WindowsSmokeTests/TrayMenuDriver.cs`
    - new: `docs/ADR/0027-pin-memory-and-widget-retry.md`, `src/DynamicIsland.App/PinMemory.cs`, `tests/DynamicIsland.IntegrationTests/PinMemoryTests.cs`, `tests/DynamicIsland.WindowsSmokeTests/PinSmokeTests.cs`
    - Suggested commit message: `feat: remember the pin and retry failed widgets (P05-02, ADR 0027)`
- 2026-10-26 Title removal + scroll fix committed by the owner as `d5ba82b`; CI run 40356940917 **success**: Release 650/650, Debug 659/659, 0 skipped.
- 2026-10-26 P05-01 + system-stats tab fix committed by the owner as `6edc4a6`; CI run 40354693505 **success**: Release 648/648, Debug 657/657, 0 skipped.
- 2026-10-26 **Owner requests: no "Dynamic Island" title; scroll bar flashed when switching to the Clock tab — fixed** (uncommitted, with P05-01).
  - Title removed: `NotchView.xaml` expanded panel is one row (tabs left, pin right; pin 30×30 to match the tabs), then the selected widget. With one widget the tabs hide and the pin stays.
  - Scroll flash cause: the ScrollViewer was `Auto`, so while the island animated up to a taller tab the content overflowed for ~200 ms and a bar showed. Fix: `NotchView.FitExpandedHeight(width, scale)` measures, allows scrolling only when the tab is taller than 360 DIP, and returns the clamped height; `IslandWindow.ExpandedHeight` uses it. ADR 0025 §1 and Consequences amended; TB4 added.
  - Tests: new `VolumeAndLayoutTests.Only_content_taller_than_the_maximum_can_scroll`; `NotchVisualTests` regenerated `notch-expanded.png`, `notch-pinned.png`, `notch-pinned-light.png` (title gone) and new `notch-pinned-tabs.png` (real clock/stats/volume views, clock selected), checked by eye.
  - Changed files: `src/DynamicIsland.App/{NotchView.xaml, NotchView.xaml.cs, IslandWindow.xaml.cs}`, `tests/DynamicIsland.IntegrationTests/{VolumeAndLayoutTests.cs, NotchVisualTests.cs, References/notch-expanded.png, References/notch-pinned.png, References/notch-pinned-light.png, References/notch-pinned-tabs.png}`, `docs/ADR/0025-tabbed-expanded-island.md`, `docs/TEST_MATRIX.md`. Suggested commit message: `fix: remove island title, no scroll bar flash on tab switch (ADR 0025)`
- 2026-10-26 **Owner report: "system stats widget is checked but not shown" — fixed** (uncommitted, with P05-01).
  - Cause (from the tabs change, ADR 0025 §4): stats sample only while their tab is shown, but a tab exists only for a widget with content, so the stats widget never got a tab. Only `MetricsWidget` is gated this way.
  - Fix: `MetricsWidget.RunAsync` publishes a placeholder snapshot on start (no readings; up time from the tick count, not a sample); sampling still starts only when its tab is shown. ADR 0025 §4 amended.
  - Tests: `MetricsWidgetTests.Collapsed_island_takes_no_samples` now expects the placeholder (0 samples). New integration `ClockWidgetPresenterTests.System_stats_get_a_tab_before_their_tab_was_ever_shown` (real registry, presenter, Win32 metrics): **failed on the old `MetricsWidget`** ("The system stats tab must appear…"), passes with the fix.
  - Changed files: `src/DynamicIsland.Widgets/MetricsWidget.cs`, `tests/DynamicIsland.UnitTests/Widgets/MetricsWidgetTests.cs`, `tests/DynamicIsland.IntegrationTests/ClockWidgetPresenterTests.cs`, `docs/ADR/0025-tabbed-expanded-island.md`. Suggested commit message (one commit with P05-01 is fine): `fix: system stats tab missing until shown (ADR 0025)`
- 2026-10-26 **P05-01 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0026**.
  - Found: the Theme setting (Use Windows setting / Light / Dark) was saved since P03-03 but never applied; colors were literals in XAML and in every widget view. At 200 % size on a small screen the expanded island could be cut off (only the window rectangle was clamped).
  - **Core:** `Shell/IslandTheme.cs`: `IslandPalette` tokens (surface, primary/secondary text, accent, fills, focus ring, placeholder), `Dark` (identical to the old colors) and `Light` palettes, shape tokens, `Resolve(mode, windowsLight)`, WCAG `ContrastRatio`. `IslandLayout.SafeScale`: the largest island fits 90 % × 60 % of the work area, never below 0.5; the setting itself is untouched.
  - **Platform.Windows:** `Display/WindowsAppTheme.cs` reads `AppsUseLightTheme` (HKCU); change message "ImmersiveColorSet".
  - **App:** `ThemeResources.cs` (token keys, `Apply`, `Themed` extension). `NotchView` applies Dark in its constructor and has `ApplyTheme`/`Palette`; XAML uses `DynamicResource`. Clock, media (incl. the shared icon-button style), metrics, volume, tasks, notes and shared parts use resource references instead of literals (`WidgetView.Primary/Secondary` and `PersonalDataParts.FieldBackground` removed). `IslandWindow`: `EffectiveScale` from `SafeScale` on each placement; theme applied on start, on settings change and on `WM_SETTINGCHANGE` "ImmersiveColorSet". Settings › Appearance: hint under Size.
  - **Tests:** unit `IslandThemeTests` (resolve, contrast ≥ 4.5 text / ≥ 3 accent and focus in both palettes, WCAG reference values, safe-scale cases and bad input); integration `ThemeTests` (switching restyles the notch and an existing widget view in place); `NotchVisualTests` now also renders Light collapsed and pinned (new references, checked by eye); the 4 dark references regenerated **byte-identical**.
  - **Changed files:**
    - modified: `docs/ADR/README.md`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md` (TH1–TH3), `docs/WORK_LOG.md`, `src/DynamicIsland.App/{ClockView.cs, IslandWindow.xaml.cs, MediaView.cs, MetricsView.cs, NotchView.xaml, NotchView.xaml.cs, NotesView.cs, PersonalDataViews.cs, SettingsWindow.xaml, TasksView.cs, VolumeView.cs, WidgetView.cs}`, `src/DynamicIsland.Core/Shell/IslandStates.cs`, `tests/DynamicIsland.IntegrationTests/NotchVisualTests.cs`
    - new: `docs/ADR/0026-theme-tokens-and-safe-size.md`, `src/DynamicIsland.App/ThemeResources.cs`, `src/DynamicIsland.Core/Shell/IslandTheme.cs`, `src/DynamicIsland.Platform.Windows/Display/WindowsAppTheme.cs`, `tests/DynamicIsland.IntegrationTests/ThemeTests.cs`, `tests/DynamicIsland.IntegrationTests/References/{notch-collapsed-light.png, notch-pinned-light.png}`, `tests/DynamicIsland.UnitTests/Shell/IslandThemeTests.cs`
    - Suggested commit message: `feat: theme tokens, light/dark island and safe size (P05-01, ADR 0026)`
- 2026-10-26 G04 evidence + tab fix committed by the owner as `706daf7`; CI run 40342549483 **success**: Release 624/624, Debug 633/633, 0 skipped (includes the 100-restart smoke test). **G04 automated PASS.**
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

## TEST EVIDENCE (current task; older rows in `docs/WORK_LOG.md`)
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P03-01 | GitHub Actions `windows-2025` | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40208475923 |
| 2026-10-25 | P03-02 | Windows 11 Pro 25H2 x64, 1 monitor at 100 % | `dotnet build` Release + Debug | PASS | 0 warnings (earlier attempts failed on IDE0005; fixed). One Debug build hit a transient "temporary files folder is full / insufficient resources" MSBuild error: disk 27 GB free, RAM 7.9 GB free, no leaks found; the rebuild passed. |
| 2026-10-25 | P03-02 | same | Unit tests | PASS | 322/322 |
| 2026-10-25 | P03-02 | same | Smoke project alone: Debug 18 tests ×4, Release 14 ×4 | PASS (some skipped) | All tray actions pass, including restart 3/4 and Safe Mode. The fullscreen test was **skipped** in some runs (Windows refused foreground). |
| 2026-10-26 | P05-02 | Windows 11 Pro 25H2 x64, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | **2 FAILs → fixed** | Release 664/664 ×2; Debug 673/674 ×2: `Try_again_restarts_only_a_failed_enabled_widget` (retry race, product fix) and `TrayActionSmokeTests.Pause…` (menu closed early, driver fix) |
| 2026-10-26 | P05-02 fixes | same | Rebuild, then the full suite Release ×2 and Debug ×2 | PASS | Release 664/664 ×2, Debug 674/674 ×2, **0 skipped** |
| 2026-10-26 | title + scroll fix | GitHub Actions `windows-2025` (`d5ba82b`) | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40356940917; 650/650 and 659/659, 0 skipped |
| 2026-10-26 | title + scroll fix | Windows 11 Pro 25H2 x64, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS | Release 650/650 ×2, Debug 659/659 ×2, **0 skipped** |
| 2026-10-26 | P05-01 + stats fix | GitHub Actions `windows-2025` (`6edc4a6`) | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40354693505 |
| 2026-10-26 | stats tab fix | Windows 11 Pro 25H2 x64, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2 | PASS | Release 648/648 ×2, Debug 657/657 ×2, **0 skipped** (P05-01 + stats fix together) |
| 2026-10-26 | P05-01 | Windows 11 Pro 25H2 x64, 1 monitor at 100 %, `CI=true` | `dotnet build` Release + Debug (0 warnings), then the full suite `--max-parallel-test-modules 1`, Release ×2 and Debug ×2; after the Settings hint, rebuild and Release ×1 + Debug ×1 | PASS | Release 647/647 ×3, Debug 656/656 ×3, **0 skipped** |
| 2026-10-26 | G04 + tab fix | GitHub Actions `windows-2025` (`706daf7`) | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40342549483; 624/624 and 633/633, 0 skipped |
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
