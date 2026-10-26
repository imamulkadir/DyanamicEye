# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01 is done (G01 PASS, confirmed on CI run 40201274900). P02-01 is done; P02-02..P02-05 are implemented and **G02 waits for the owner's hardware matrix**. P03 is in progress with owner approval; P03-01…P03-05 are implemented (awaiting manual validation); **G03 waits for the owner's hardware checks**. P04 started with owner approval while G02/G03 stay open.
- **Active task:** next is P04-06 (SQLite notes and tasks). P04-05 (volume widget, persistent views, content-sized island; ADR 0022) is IMPLEMENTED_AWAITING_VALIDATION. G02/G03 manual rows are still owed by the owner.
- **Active task status:** P04-05 IMPLEMENTED_AWAITING_VALIDATION (2026-10-26), **uncommitted** (the owner commits). P04-04 is committed as `5f13f25`, and CI 40310973120 is green with 0 skipped.
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
- **Last updated:** 2026-10-26 (P04-05 IMPLEMENTED_AWAITING_VALIDATION; next P04-06)

## NEXT ACTION (ONE task only)
**Owner first:** review and commit the P04-05 changes (file list in RECENT VERIFIED CHANGES), push, and check that CI is green. VO1–VO3 with real devices are the valuable manual rows.
**Then agent: P04-06** (guide §5): SQLite notes and tasks.
- CRUD, edits, completion, timestamps and validation; transactional changes; search is optional; no network.
- Note/task text is personal data: never log it (ADR 0004).
- This needs a dependency decision: `Microsoft.Data.Sqlite` (MIT, from Microsoft). Verify the current version, licence, native bundle (`SQLitePCLRaw.bundle_e_sqlite3`) and size, and record them in an ADR together with the schema, migrations, DB path (`%LOCALAPPDATA%\DynamicIsland\data\islands.db`) and corruption/backup behaviour.
- **Ask the owner to approve the new package before adding it** (AGENTS.md: avoid dependency churn).
- Views: compact note and task lists in the expanded island, plus editing. Keyboard: Enter to add, Space to complete.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | DONE | **G01 PASS** 2026-10-25, CI run 40201274900 (Release + Debug). See the G01 evidence below. |
| P02 — Island shell | P02-01…P02-05 | IN_PROGRESS (01 DONE; 02–05 IMPLEMENTED_AWAITING_VALIDATION) | G02 pending (needs a real multi-DPI/multi-monitor check) |
| P03 — Tray/settings/startup | P03-01…P03-05 | IN_PROGRESS (01–05 IMPLEMENTED_AWAITING_VALIDATION; started with owner approval while G02 is open) | G03 pending (owner hardware checks) |
| P04 — Core widgets/data | P04-01…P04-07 | IN_PROGRESS (01–05 IMPLEMENTED_AWAITING_VALIDATION; 06 next; started with owner approval while G02/G03 are open) | G04 pending |
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
- 2026-10-26 P04-04 committed by the owner as `5f13f25`; CI run 40310973120 **success**: Release 562/562, Debug 569/569, 0 skipped.
- 2026-10-26 **P04-04 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0021**.
  - **Core:**
    - New `Metrics/SystemMetrics.cs`: `ISystemMetricsService`, `CpuTimes`, `MemoryStatus`, `BatteryStatus`, plus the pure helpers `CpuUsage.Between` and `MetricsFormat`.
    - `IWidget.SetExpanded` (default no-op); `WidgetRegistry.SetExpanded`.
    - `IslandLayout.Expanded` → 420×**252**.
  - **Platform.Windows:** new `Metrics/Win32SystemMetrics.cs` (`GetSystemTimes`, `GlobalMemoryStatusEx`, `GetSystemPowerStatus` with no-battery/unknown handling, `Environment.TickCount64`); `NativeMethods` imports.
  - **Widgets:** new `MetricsWidget` + `MetricsSnapshot`.
    - Samples every 2 s **only while expanded and on screen**; none while collapsed, hidden or paused.
    - On expanding it takes a baseline and shows CPU after 0.5 s ("—" before that).
    - Resume resets the baseline; failed readings leave that value out.
    - `BuiltInWidgets.Create(..., metrics)`.
  - **App:**
    - New `MetricsView` (tiles: CPU, memory used/total GB, battery with ⚡ only when a battery exists, up time; UIA names).
    - Presenter forwards `SetExpanded` from the island state and registers the view; `AppHost` wiring.
  - **Tests:**
    - Unit: `Widgets/MetricsWidgetTests.cs` (math and format: 9 cases; cadence: 5).
    - Integration: `MetricsIntegrationTests` (real Win32 readings are plausible; tiles with and without a battery).
    - Visual references notch-expanded/pinned **regenerated** at 252 px; a 3-widget rendering was checked by eye.
  - **Changed files for the owner:**
    - modified: `docs/ADR/README.md`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md` (SM1–SM4), `docs/WORK_LOG.md` (older entries archived), `src/DynamicIsland.App/{AppHost.cs, WidgetPresenter.cs}`, `src/DynamicIsland.Core/Shell/IslandStates.cs`, `src/DynamicIsland.Core/Widgets/WidgetRegistry.cs`, `src/DynamicIsland.Platform.Windows/NativeMethods.cs`, `src/DynamicIsland.Widgets/BuiltInWidgets.cs`, `tests/DynamicIsland.IntegrationTests/{ClockWidgetPresenterTests.cs, References/notch-expanded.png, References/notch-pinned.png}`
    - new: `docs/ADR/0021-metrics-widget.md`, `src/DynamicIsland.App/MetricsView.cs`, `src/DynamicIsland.Core/Metrics/SystemMetrics.cs`, `src/DynamicIsland.Platform.Windows/Metrics/Win32SystemMetrics.cs`, `src/DynamicIsland.Widgets/MetricsWidget.cs`, `tests/DynamicIsland.IntegrationTests/MetricsIntegrationTests.cs`, `tests/DynamicIsland.UnitTests/Widgets/MetricsWidgetTests.cs`
    - Suggested commit message: `feat(widgets): system metrics widget sampling only while expanded (P04-04, ADR 0021)`
- 2026-10-26 P04-03 committed by the owner as `179cef8`; CI run 40309259229 **success**: Release 545/545, Debug 552/552, 0 skipped.
- 2026-10-26 **Owner-requested real-player check (P04-03)** with `D:\Music\Music\Avenged Sevenfold - Bat Country ... (320 KBps).mp3` in Media Player (Microsoft.ZuneMusic). Temporary probes using the app's real adapter and view, deleted afterwards.
  - **Bug 1 fixed: album art was missing.** Media Player hands out a **768×768 32-bit BMP of 2,359,446 bytes**; the 2 MB cap skipped it, and the placeholder showed. `MediaWidget.MaxArtBytes` is now 12 MB. Rendered by eye after the fix: the art shows.
  - **Bug 2 fixed: a blank artist line.** The file has no artist tag, which left an empty second line. The view now leaves the line out and wraps the title to 2 lines.
  - **Next/Previous:** a single opened file has only Pause (the player disables Next/Previous: correct, our buttons are dimmed).
    - With a queue (Avash and Adele added via the shell verb "Add to Media Player play queue"), Next went Bat Country → Avash and Previous came back, through our adapter. Previous was dimmed on the first track.
    - Media Player sometimes **refused** an advertised Next for a moment (seen right after track changes; once over 1 s). The adapter now retries a refused, enabled command up to 5×250 ms. Unsupported commands are not retried.
  - **Side effects on the owner's PC:**
    - Media Player is left playing, with 2 tracks added to its queue.
    - A `.m3u` attempt opened a Windows "Pick an app" dialog. I closed it (the dialog process was stopped after it ignored the close request) and chose no app. No file associations were changed.
  - New tests: the art cap admits that BMP size (unit); an untagged track has a single wrapped title and no empty line (integration).
  - **Owner follow-up (screenshots):** "Previous (not supported by this player)" on Bat Country and the same for "Next" on Adele. The cause is the queue position: Media Player turns Previous off on the first queued track and Next off on the last. The tooltip wording was wrong, so it now reads "… (not available right now)" (`MediaView.cs`, ADR 0020, TEST_MATRIX MD2). The Debug build was locked by the owner's running Debug app (PID 6272), which was not touched, so this was verified on Release: unit 455/455, integration 75/75, 0 warnings.
- 2026-10-26 **P04-03 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0020**. Verified against the official Microsoft Learn docs: "Call Windows Runtime APIs in desktop apps" (2026-10-03) and the GSMTC manager reference (1809+).
  - **Build:**
    - TFM `net10.0-windows10.0.19041.0` for Platform.Windows, App, IntegrationTests and WindowsSmokeTests; `packages.lock.json` of those 4 only renames the TFM key.
    - The targeting pack `Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57 is selected by the pinned SDK. The output gains `Microsoft.Windows.SDK.NET.dll` (24 MB) and `WinRT.Runtime.dll`.
    - Output folders are now `bin\<config>\net10.0-windows10.0.19041.0\`. TEST_MATRIX's run path and ADR 0002 are updated.
  - **Core:**
    - New `Media/MediaContracts.cs` (`IMediaSessionService`, `MediaInfo`, `MediaPlaybackState`, `MediaControls`, `MediaCommand`).
    - `IWidget.ExecuteAsync` (default false); `WidgetRegistry.ExecuteAsync` (only for running widgets).
    - `IslandLayout.Expanded` 420×180 → **420×212**.
  - **Platform.Windows:** new `Media/WindowsMediaSessionService.cs`.
    - Follows the current GSMTC session through events; no polling.
    - Closed sessions and sessions without title/artist mean nothing playing.
    - Counts sessions; maps the player's controls; art over 12 MB is refused (was 2 MB, see the real-player check below).
    - WinRT errors mean no session or a refused command, never a throw.
  - **Widgets:** new `MediaWidget` + `MediaSnapshot`.
    - An event loop with a 60 ms settle; art is re-read only when the track changes.
    - Degraded `no-media` or `media-unavailable` (idles, no retry loop); commands previous/play-pause/next.
    - Registered in `BuiltInWidgets.Create(settings, time, media)`.
  - **App:**
    - New `MediaView` (art, title, artist, "· N more playing", 3 icon buttons that are disabled with a tooltip when unsupported; AutomationIds `Media.*`; focus ring).
    - New `AlbumArtCache` (decodes off the UI thread at 96 px and freezes; keeps only the current image).
    - `WidgetPresenter.Views` now receives the presenter (`Execute`, `AlbumArt`); `AppLog` 1059 WidgetCommandFailed.
    - `NotchView` hides the top-row time while the expanded panel shows the large clock (no duplicate time).
  - **Tests:**
    - Unit: `Widgets/MediaWidgetTests.cs` (7).
    - Integration: `MediaWidgetIntegrationTests` (2). One runs the **real Windows adapter read-only, with no commands sent to the user's player**. The other runs a scripted service through the real registry, presenter and view: the art decodes, the unsupported button is disabled, and the clicks reach the service.
    - Clock presenter test adjusted for two widgets.
    - Visual references **notch-expanded.png and notch-pinned.png regenerated** (new height) and checked by eye.
    - Real Release app: widget-media went Starting → Degraded `no-media`; no track text in the log; exit 0.
  - **Changed files for the owner:**
    - modified: `docs/ADR/{0002-solution-layout-and-test-platform.md, README.md}`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md`, `src/DynamicIsland.App/{AppHost.cs, AppLog.cs, DynamicIsland.App.csproj, NotchView.xaml.cs, WidgetPresenter.cs, packages.lock.json}`, `src/DynamicIsland.Core/Shell/IslandStates.cs`, `src/DynamicIsland.Core/Widgets/WidgetRegistry.cs`, `src/DynamicIsland.Platform.Windows/{DynamicIsland.Platform.Windows.csproj, packages.lock.json}`, `src/DynamicIsland.Widgets/BuiltInWidgets.cs`, `tests/DynamicIsland.IntegrationTests/{ClockWidgetPresenterTests.cs, DynamicIsland.IntegrationTests.csproj, packages.lock.json, References/notch-expanded.png, References/notch-pinned.png}`, `tests/DynamicIsland.WindowsSmokeTests/{DynamicIsland.WindowsSmokeTests.csproj, packages.lock.json}`
    - new: `docs/ADR/0020-media-widget.md`, `src/DynamicIsland.App/{AlbumArtCache.cs, MediaView.cs}`, `src/DynamicIsland.Core/Media/MediaContracts.cs`, `src/DynamicIsland.Platform.Windows/Media/WindowsMediaSessionService.cs`, `src/DynamicIsland.Widgets/MediaWidget.cs`, `tests/DynamicIsland.IntegrationTests/MediaWidgetIntegrationTests.cs`, `tests/DynamicIsland.UnitTests/Widgets/MediaWidgetTests.cs`
    - Suggested commit message: `feat(widgets): media widget over Windows media sessions (P04-03, ADR 0020)` (includes the real-player fixes)
- 2026-10-26 P04-02 committed by the owner as `abd5693`; CI run 40303146979 **success**: Release 534/534, Debug 541/541, 0 skipped.
- 2026-10-26 **P04-02 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0019**; ADR 0016 §4/§6 are amended. No countdown timers or stopwatch: they were offered to the owner, who did not ask for them (guide: "timers only if approved").
  - **Core:** `IWidget.SetActive`/`OnSystemChanged` (default no-ops); `WidgetRegistry.SetActive/NotifySystemChanged/Dispose`.
  - **Widgets:**
    - New `ClockWidget` + `ClockSnapshot`: ticking on the `TimeProvider` aligned to the minute or second, waking on settings, activation and system changes; inactive means no ticks. Uses `ClockFormatter` (ADR 0016) and the long date.
    - `BuiltInWidgets.Create(settings, time)` returns the clock.
  - **App:**
    - **`NotchClock.cs` deleted.** `WidgetPresenter` now sets the notch text from the clock slot (blank when off, failed or in Safe Mode) and renders the expanded clock view (34 px light time plus the long date).
    - The presenter maps island visibility to `SetActive` and forwards `SystemEvents` (time, locale, resume).
    - `AppHost`/`App.xaml.cs` wiring updated.
  - **Tests:**
    - **`NotchClockTests.cs` deleted.**
    - Unit: `Widgets/ClockWidgetTests.cs` (6, real time, bounded): immediate publish with the options and date; at most 2 publishes in 1.5 s by default; 3–5 in 2.5 s with seconds; inactive means no ticks, then catch-up; settings and system changes refresh at once; stop and release.
    - Integration: `ClockWidgetPresenterTests` (real registry, widget, presenter and NotchView): the notch shows the compact text, the panel shows the time and date, and disabling clears both.
    - An expanded rendering was checked by eye.
  - **Process note:** while removing the two files I ran `git rm`, which staged the deletions against the no-git-writes rule. I immediately unstaged them with `git reset -q -- <the two paths>`; the index again matches HEAD, and the files are only deleted in the working tree.
  - **Changed files for the owner:**
    - modified: `docs/ADR/{0016-notch-clock.md, README.md}`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md` (CW1–CW3), `docs/WORK_LOG.md` (older entries archived), `src/DynamicIsland.App/{App.xaml.cs, AppHost.cs, WidgetPresenter.cs}`, `src/DynamicIsland.Core/Widgets/WidgetRegistry.cs`, `src/DynamicIsland.Widgets/BuiltInWidgets.cs`
    - deleted: `src/DynamicIsland.App/NotchClock.cs`, `tests/DynamicIsland.IntegrationTests/NotchClockTests.cs`
    - new: `docs/ADR/0019-clock-widget.md`, `src/DynamicIsland.Widgets/ClockWidget.cs`, `tests/DynamicIsland.IntegrationTests/ClockWidgetPresenterTests.cs`, `tests/DynamicIsland.UnitTests/Widgets/ClockWidgetTests.cs`
    - Suggested commit message: `feat(widgets): clock and date widget drives notch and expanded view (P04-02, ADR 0019)`
- 2026-10-26 P04-01 committed by the owner as `294a8f0`; CI run 40301030072 **success**: Release 533/533, Debug 540/540, 0 skipped.

## TEST EVIDENCE (current task; older rows in `docs/WORK_LOG.md`)
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P03-01 | GitHub Actions `windows-2025` | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40208475923 |
| 2026-10-25 | P03-02 | Windows 11 Pro 25H2 x64, 1 monitor at 100 % | `dotnet build` Release + Debug | PASS | 0 warnings (earlier attempts failed on IDE0005; fixed). One Debug build hit a transient "temporary files folder is full / insufficient resources" MSBuild error: disk 27 GB free, RAM 7.9 GB free, no leaks found; the rebuild passed. |
| 2026-10-25 | P03-02 | same | Unit tests | PASS | 322/322 |
| 2026-10-25 | P03-02 | same | Smoke project alone: Debug 18 tests ×4, Release 14 ×4 | PASS (some skipped) | All tray actions pass, including restart 3/4 and Safe Mode. The fullscreen test was **skipped** in some runs (Windows refused foreground). |
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
