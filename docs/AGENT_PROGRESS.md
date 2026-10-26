# AGENT_PROGRESS.md — Persistent AI Agent Handoff

> **Keep this file accurate on every session.** This is the durable checkpoint that lets a new agent resume after a context reset. Update it alongside source changes, and never mark anything DONE without evidence. Move older detail to `docs/WORK_LOG.md` so this file stays short.

## CURRENT STATUS (read first)
- **Overall:** IN_PROGRESS. P00 is done (G00 PASS). P01 is done (G01 PASS, confirmed on CI run 40201274900). P02-01 is done; P02-02..P02-05 are implemented and **G02 waits for the owner's hardware matrix**. P03 is in progress with owner approval; P03-01…P03-05 are implemented (awaiting manual validation); **G03 waits for the owner's hardware checks**.
- **Active task:** none in progress. P03-05 (optional global shortcut; ADR 0017) is IMPLEMENTED_AWAITING_VALIDATION, which completes P03. Gates G02 and G03 are owed by the owner (TEST_MATRIX: G02 rows, Y1–Y7, TA1–TA10, SW1–SW8, ST1–ST7, CK1–CK5, SC1–SC4).
- **Active task status:** P03-05 IMPLEMENTED_AWAITING_VALIDATION (2026-10-26), **uncommitted** (the owner commits). P03-04 and the notch clock are committed as `8917a21`, and CI 40296571628 is green with 0 skipped.
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
- **Last updated:** 2026-10-26 (P03-05 IMPLEMENTED_AWAITING_VALIDATION; P03 complete; G02/G03 owner checks next)

## NEXT ACTION (ONE task only)
**Owner:**
1. Review and commit the P03-05 changes (file list in RECENT VERIFIED CHANGES), push, and check that CI is green.
2. Gates: run the G02 and G03 manual rows in `docs/TEST_MATRIX.md` on real hardware. The G03 criteria are: Explorer restart restores the tray; sign-in starts one collapsed island when opted in; the user can fully exit, change monitor and reset an off-screen position. Then do one of these:
   - record the results;
   - or explicitly approve continuing to P04 while the gates stay open, as was done for P03.

**Agent, only after that approval:** start **P04-01** (guide §5): the widget registry and lifecycle.
- Built-in, compiled-in modules as `IIsolatedComponent`s (ADR 0007), using the existing `WidgetSetting` enable/order data.
- Failures are isolated; memory and background work stop when a widget is disabled.
- The notch clock (ADR 0016) becomes the clock widget's collapsed view.

## TASK BOARD (compact; detailed definitions live in the guide)

| Phase | Task IDs | Status | Gate evidence |
|---|---|---|---|
| P00 — Environment and baseline | P00-01…P00-04 | DONE | **G00 PASS** 2026-10-25, run 40196393380 (`963b01c`) |
| P01 — Reliability kernel | P01-01…P01-05 | DONE | **G01 PASS** 2026-10-25, CI run 40201274900 (Release + Debug). See the G01 evidence below. |
| P02 — Island shell | P02-01…P02-05 | IN_PROGRESS (01 DONE; 02–05 IMPLEMENTED_AWAITING_VALIDATION) | G02 pending (needs a real multi-DPI/multi-monitor check) |
| P03 — Tray/settings/startup | P03-01…P03-05 | IN_PROGRESS (01–05 IMPLEMENTED_AWAITING_VALIDATION; started with owner approval while G02 is open) | G03 pending (owner hardware checks) |
| P04 — Core widgets/data | P04-01…P04-07 | NOT_STARTED | G04 pending |
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
- 2026-10-26 **P03-05 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0017**.
  - **Core:**
    - New `Shell/Hotkey.cs`: `Hotkey.TryParse`/`ToString`/`VirtualKey` (keys A–Z, 0–9, F1–F24, Space; **at least 2 modifiers**; canonical text "Ctrl+Alt+Shift+Win+Key"; `HotkeyModifiers` = Win32 MOD_* values).
    - `ShortcutAction.For/Opens`: Hidden → Show+Click; Paused → Resume+Click; Collapsed/Peek → Click; Expanded/Pinned → Escape; fullscreen → nothing.
    - `BehaviorSettings.Shortcut` (`ShortcutSettings`: `Enabled` false, `Keys` "Ctrl+Alt+I"). Additive, no schema bump. `BehaviorSettings` equality includes it.
    - `SettingsValidator` canonicalizes the keys or falls back to the default keys.
    - `SettingsEditor`: `ShortcutEnabled`/`ShortcutText`/`ShortcutError` (merged per field, reset with Behavior).
  - **Platform.Windows:**
    - New `Input/Win32Hotkey.cs`: `RegisterHotKey` with MOD_NOREPEAT; `Registered`/`InUse` (error 1409)/`Failed`.
    - `NativeMethods` Register/UnregisterHotKey; `IslandWindowInterop.FindMessageWindow` and `Post`.
  - **App:**
    - New `ShortcutController`: a message-only window, registers only when enabled, re-registers on change, and activates the island before expanding.
    - `AppLog` 1056/1057, without the key combination.
    - `AppHost`/`App.xaml.cs` wiring.
    - `SettingsWindow` Behavior: a check box, a shortcut field with validation, and the registration status (on / in use / failed / off).
  - **Tests:**
    - Unit: `Shell/HotkeyTests.cs` (35 cases: parsing, rules, virtual keys, actions end Expanded, validator) and an editor shortcut test.
    - Integration: `Win32HotkeyTests` (a real registration, a conflict from a second window reported as InUse, release) and a settings window shortcut test.
    - Smoke (Debug): `ShortcutSmokeTests`. With the shortcut enabled in isolated settings, a second registration from the test is refused (proof that Windows granted it to the app). A posted WM_HOTKEY opens the island ("Dynamic Island, expanded") and the next one closes it. Released on exit.
  - **Docs:** ADR 0017 (indexed); TEST_MATRIX SC1–SC4.
  - **Changed files for the owner:**
    - modified: `docs/ADR/README.md`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md`, `docs/WORK_LOG.md` (P03-01/02 entries archived), `src/DynamicIsland.App/{App.xaml.cs, AppHost.cs, AppLog.cs, SettingsWindow.xaml, SettingsWindow.xaml.cs}`, `src/DynamicIsland.Core/Settings/{AppSettings.cs, SettingsValidator.cs}`, `src/DynamicIsland.Core/Shell/SettingsEditor.cs`, `src/DynamicIsland.Platform.Windows/{Display/IslandWindowInterop.cs, NativeMethods.cs}`, `tests/DynamicIsland.IntegrationTests/SettingsWindowTests.cs`, `tests/DynamicIsland.UnitTests/Shell/SettingsEditorTests.cs`
    - new: `docs/ADR/0017-global-shortcut.md`, `src/DynamicIsland.App/ShortcutController.cs`, `src/DynamicIsland.Core/Shell/Hotkey.cs`, `src/DynamicIsland.Platform.Windows/Input/Win32Hotkey.cs`, `tests/DynamicIsland.IntegrationTests/Win32HotkeyTests.cs`, `tests/DynamicIsland.UnitTests/Shell/HotkeyTests.cs`, `tests/DynamicIsland.WindowsSmokeTests/ShortcutSmokeTests.cs`
    - Suggested commit message: `feat(shortcut): optional global shortcut via RegisterHotKey (P03-05, ADR 0017)`
- 2026-10-26 P03-04 + notch clock committed by the owner as `8917a21`; CI run 40296571628 **success**: Release 488/488, Debug 494/494, 0 skipped.
- 2026-10-26 **Owner request: notch clock — IMPLEMENTED_AWAITING_VALIDATION** (uncommitted). Decisions are in **ADR 0016**; the ADR 0009 sensor housing is superseded. This is the collapsed part of P04-02, taken ahead of P03-05 at the owner's request.
  - The owner's words: the notch showed a "dept camera, front camera icon and the front speaker icon", which a Windows PC does not have. Keep the design, but show the time (e.g. 2:42 PM), with options for seconds and for dates in different formats.
  - **Core:**
    - `Settings/AppSettings.cs`: `AppearanceSettings.Clock` (`ClockSettings`: `HourFormat` System/TwelveHour/TwentyFourHour, `ShowSeconds`, `DateFormat` None/Weekday/Date/WeekdayAndDate/Numeric). Additive, so no schema bump (ADR 0003 §7).
    - `SettingsValidator` repairs a null clock or unknown enum values.
    - New `Shell/ClockFormatter.cs` (`Format`, `DelayUntilNextChange`).
    - `SettingsEditor`: clock fields (reset with the Appearance page) and `ClockPreview`. The culture parameter is now `CultureInfo`.
  - **App:**
    - `NotchView`: the sensor housing is replaced by a centred `ClockText`; the font and the clock row scale with the appearance size; `SetClock`/`ClockDisplay`.
    - New `NotchClock`: a one-shot `DispatcherTimer` aligned to the next minute or second. It stops while the island is off-screen and refreshes on settings, state, time, time-zone, region and resume changes. It is hidden when the "Clock and date" widget is off.
    - `IslandWindow.NotchView`; `AppHost` and `App.xaml.cs` wiring.
    - `SettingsWindow` Appearance → Clock: time format, Show seconds, date, with examples in the user's format and a live preview.
  - **Tests:**
    - Unit: `Shell/ClockFormatterTests.cs` (10 cases including de-DE, a region without AM/PM, the tick delay and validation) and an editor clock test.
    - Integration: `NotchClockTests.cs` (6, real `NotchView` with a fixed `TimeProvider`).
    - Visual: the 4 notch reference PNGs were **regenerated** (now with a fixed "2:42 PM") and reviewed by eye.
    - Real desktop: a Release app capture showed "2:54 PM" in the notch, and the app exited 0.
  - **Changed files (clock):**
    - modified: `docs/ADR/{0009-island-states-and-interaction.md, README.md}`, `docs/TEST_MATRIX.md` (CK1–CK5), `src/DynamicIsland.App/{App.xaml.cs, AppHost.cs, IslandWindow.xaml.cs, NotchView.xaml, NotchView.xaml.cs, SettingsWindow.xaml, SettingsWindow.xaml.cs}`, `src/DynamicIsland.Core/Settings/{AppSettings.cs, SettingsValidator.cs}`, `src/DynamicIsland.Core/Shell/SettingsEditor.cs`, `tests/DynamicIsland.IntegrationTests/NotchVisualTests.cs`, `tests/DynamicIsland.IntegrationTests/References/notch-*.png`, `tests/DynamicIsland.UnitTests/Shell/SettingsEditorTests.cs`
    - new: `docs/ADR/0016-notch-clock.md`, `src/DynamicIsland.App/NotchClock.cs`, `src/DynamicIsland.Core/Shell/ClockFormatter.cs`, `tests/DynamicIsland.IntegrationTests/NotchClockTests.cs`, `tests/DynamicIsland.UnitTests/Shell/ClockFormatterTests.cs`
    - Suggested combined commit message: `feat: first-run onboarding, sign-in startup (P03-04, ADR 0015) and notch clock (ADR 0016)`
- 2026-10-26 **P03-04 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0015**; ADR 0014 §6 is amended.
  - **Core:**
    - New `Startup/StartupPolicy.cs` with `IStartupRegistration` (the guide §3 contract), `StartupRegistrationStatus`, `StartupPolicy.Decide/Describe/BuildCommand/ExecutableOf`, and `StartupState`.
    - Rule: an entry for another copy is taken over only when the user just enabled startup here, or when that copy's launcher no longer exists.
    - New `Shell/Onboarding.cs` (`IsNeeded`, `Apply`).
    - `LaunchArguments` and `LauncherArguments` accept `--startup`; `LauncherContract.StartupDelay` = 5 s; `TestHooks.RegistryRootVariable` added.
    - `SettingsEditor`: editable `StartWithWindows`/`DailyUpdateChecks` boxes. An untouched box keeps `NotAsked`; Reset never changes them. `WidgetLabel` is now public.
  - **Platform.Windows:** new `Startup/RegistryStartupRegistration.cs`.
    - Writes only HKCU `...\CurrentVersion\Run` value `DynamicIsland` = `"<dir>\DynamicIsland.Launcher.exe" --startup`.
    - Reads but never writes `Explorer\StartupApproved\Run` (odd first byte = turned off) and `DisableCurrentUserRun` (HKCU and HKLM).
  - **Launcher:** `--startup` waits 5 s and passes `--startup` to the first app start only.
  - **App:**
    - New `StartupCoordinator`: reconciles on every start and whenever the consent changes. Registry errors are logged by type.
    - `App.xaml.cs`:
      - A `--startup` launch without consent removes the entry and exits with 0.
      - A `--startup` launch while another instance runs exits without activating it.
      - Onboarding appears when needed, owned by the island window.
    - New `OnboardingWindow`: both consent boxes unchecked, privacy summary, widget choices; Continue saves; closing saves nothing.
    - `SettingsWindow`: real check boxes plus the registration status text (on / turned off in Windows / blocked by policy / another copy / launcher missing / not registered).
    - `AppLog` events 1050–1055.
  - **Tests:**
    - Unit `Startup/StartupPolicyTests.cs` (policy, command parsing, arguments, onboarding) and 3 new consent tests in `SettingsEditorTests`.
    - Integration:
      - `RegistryStartupRegistrationTests` (real registry under a scratch HKCU key);
      - `OnboardingWindowTests` (5);
      - a settings window startup test.
    - Smoke (Debug only): `StartupSmokeTests`.
      - Without permission, the launcher waits at least 5 s, the app exits 0, the stale entry is removed, and there is no relaunch.
      - Onboarding through UIA registers this copy's launcher; then a `--startup` start is delayed, shows the island and no onboarding.
    - `RealApp` closes the first-run window in other smoke tests (closing saves nothing) and points the Debug registry hook to a scratch key.
  - **Safety check:** after all the runs, the real `HKCU\...\Run` has no DynamicIsland value, no `Software\DynamicIsland.Tests` keys are left, and no real settings file was written.
  - **Docs:** ADR 0015 (indexed); the ADR 0014 status notes the amendment; TEST_MATRIX rows ST1–ST7.
  - **Changed files for the owner:**
    - modified: `docs/ADR/{0014-settings-window.md, README.md}`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md`, `src/DynamicIsland.App/{App.xaml.cs, AppHost.cs, AppLog.cs, SettingsWindow.xaml, SettingsWindow.xaml.cs}`, `src/DynamicIsland.Core/Diagnostics/TestHooks.cs`, `src/DynamicIsland.Core/Lifecycle/{ExitReason.cs, LaunchArguments.cs, LauncherArguments.cs}`, `src/DynamicIsland.Core/Shell/SettingsEditor.cs`, `src/DynamicIsland.Launcher/{ProcessAppRunner.cs, Program.cs}`, `tests/DynamicIsland.IntegrationTests/SettingsWindowTests.cs`, `tests/DynamicIsland.UnitTests/Shell/SettingsEditorTests.cs`, `tests/DynamicIsland.WindowsSmokeTests/RealApp.cs`
    - new: `docs/ADR/0015-first-run-and-sign-in-startup.md`, `src/DynamicIsland.App/{OnboardingWindow.xaml, OnboardingWindow.xaml.cs, StartupCoordinator.cs}`, `src/DynamicIsland.Core/Shell/Onboarding.cs`, `src/DynamicIsland.Core/Startup/StartupPolicy.cs`, `src/DynamicIsland.Platform.Windows/Startup/RegistryStartupRegistration.cs`, `tests/DynamicIsland.IntegrationTests/{OnboardingWindowTests.cs, RegistryStartupRegistrationTests.cs}`, `tests/DynamicIsland.UnitTests/Startup/StartupPolicyTests.cs`, `tests/DynamicIsland.WindowsSmokeTests/StartupSmokeTests.cs`
    - Suggested commit message: `feat(startup): first-run onboarding and per-user sign-in startup (P03-04, ADR 0015)`
- 2026-10-26 P03-03 committed by the owner as `c7bbbe4` and pushed; CI run 40290363463 **success**: Release 421/421, Debug 425/425, 0 skipped.
- 2026-10-26 **P03-03 IMPLEMENTED_AWAITING_VALIDATION** (uncommitted; the owner commits). Decisions are in **ADR 0014**.
  - **Core:**
    - New `Shell/SettingsEditor.cs`: the settings-window draft (INotifyPropertyChanged), strict validation with user messages using `SettingsValidator` limits, `IsDirty`/`CanSave`, per-page and full reset (consents are never reset), widget move, and `CreateChange()`/`ApplyTo()`, which merge only the fields the user changed onto the latest settings. Loading suppresses intermediate notifications.
    - `TrayMenu.cs`: `OrderDisplays` is shared with the settings display list, and Settings… is now enabled.
  - **App:**
    - New `SettingsWindow.xaml(.cs)` with 8 left tabs (General, Appearance, Behavior, Widgets, Updates, Privacy, Diagnostics, About).
      - Buttons: Save (default, Ctrl+S), Revert, Close (Esc).
      - Closing with unsaved changes asks Save / Don't save / Cancel. The prompt is skipped while the app shuts down or with the NoDialogs test hook.
      - A banner shows when the store is read-only. A status line (UIA live region) names the page that holds an error.
      - Diagnostics shows the `DiagnosticBundleBuilder` preview, then Save report… (a save dialog, then `WriteZipAsync`).
      - Privacy shows the data folder with an Open button. About links to the GitHub page (https only).
      - Consents are shown as read-only text.
    - New `SettingsWindowService` (one window: open or activate), `AppInfo` (version, copyright, settings load outcome for diagnostics) and `ShellLauncher` (open a folder or an https link; the tray uses it too).
    - `AppHost` and `TrayController` wiring; `App.xaml.cs` uses `AppInfo`.
    - `AppLog` adds SettingsSaved (1046), DiagnosticReportSaved (1047), DiagnosticReportFailed (1048) and OpenFolderFailed (1049).
  - **Tests:**
    - Unit: `Shell/SettingsEditorTests.cs` (31 cases) and the display-order and settings-enabled tray tests.
    - Integration: `SettingsWindowTests.cs` (9, real offscreen WPF window). Covers pages and AutomationIds; every input has a UIA name (this found 10 `Label.Target` inputs without names, now fixed); save merges with a concurrent tray change; reload without edits; validation disables Save; a failed save keeps the draft; read-only banner; display list; the diagnostic preview writes no file.
    - Smoke: `Settings_opens_once_and_closing_it_keeps_the_app_running` (real app, tray → Settings… twice, WM_CLOSE, the app stays up and exits 0).
  - **Bug found and fixed:** `Load` reported a half-loaded draft (widgets cleared) as dirty, which cleared the "Settings saved." status.
  - **Docs:** ADR 0014; TEST_MATRIX rows SW1–SW8. The P03-02 tray rows were renamed to TA1–TA10 because T1/T2 were already used by G02, and the ADR 0013 reference was updated.
  - **Changed files for the owner:**
    - modified: `docs/ADR/0013-tray-actions-settings-restart.md`, `docs/ADR/README.md`, `docs/AGENT_PROGRESS.md`, `docs/TEST_MATRIX.md`, `docs/WORK_LOG.md`, `src/DynamicIsland.App/{App.xaml.cs, AppHost.cs, AppLog.cs, TrayController.cs}`, `src/DynamicIsland.Core/Shell/TrayMenu.cs`, `tests/DynamicIsland.UnitTests/Shell/TrayMenuModelTests.cs`, `tests/DynamicIsland.WindowsSmokeTests/TrayActionSmokeTests.cs`
    - new: `docs/ADR/0014-settings-window.md`, `src/DynamicIsland.App/{AppInfo.cs, SettingsWindow.xaml, SettingsWindow.xaml.cs, SettingsWindowService.cs, ShellLauncher.cs}`, `src/DynamicIsland.Core/Shell/SettingsEditor.cs`, `tests/DynamicIsland.IntegrationTests/SettingsWindowTests.cs`, `tests/DynamicIsland.UnitTests/Shell/SettingsEditorTests.cs`
    - Suggested commit message: `feat(settings): settings window with explicit save, validation and reset (P03-03, ADR 0014)`

## TEST EVIDENCE (current task; older rows in `docs/WORK_LOG.md`)
| UTC date | Task | Host / OS | Exact command or manual scenario | Outcome | Notes |
|---|---|---|---|---|---|
| 2026-10-25 | P03-01 | GitHub Actions `windows-2025` | CI workflow | PASS | https://github.com/imamulkadir/DyanamicEye/actions/runs/40208475923 |
| 2026-10-25 | P03-02 | Windows 11 Pro 25H2 x64, 1 monitor at 100 % | `dotnet build` Release + Debug | PASS | 0 warnings (earlier attempts failed on IDE0005; fixed). One Debug build hit a transient "temporary files folder is full / insufficient resources" MSBuild error: disk 27 GB free, RAM 7.9 GB free, no leaks found; the rebuild passed. |
| 2026-10-25 | P03-02 | same | Unit tests | PASS | 322/322 |
| 2026-10-25 | P03-02 | same | Smoke project alone: Debug 18 tests ×4, Release 14 ×4 | PASS (some skipped) | All tray actions pass, including restart 3/4 and Safe Mode. The fullscreen test was **skipped** in some runs (Windows refused foreground). |
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
