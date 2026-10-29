# Windows compatibility matrix (P07-02)

What Dynamic Island v1 supports, what was tested, and how to test the rest. Status values: `PASS`, `FAIL`, `NOT_RUN`,
`N/A`. Record owner runs here and in `docs/AGENT_PROGRESS.md` with the date, Windows edition, version and build.

Sources: ADR 0001, and .NET 10 `supported-os.md` (re-checked 2026-10-29; last updated 2026-09-28, unchanged since
ADR 0001). Re-check that page before every release.

## 1. Operating systems

| Windows | Label (README, installer, About) | Installer behaviour | Status and evidence |
|---|---|---|---|
| **Windows 11 25H2 x64** | **Supported, tested** | Installs per user, no prompt | **PASS** 2026-10-28/29, agent and owner, Windows 11 Pro 25H2 build 26200.9711: full suites (Release 875, Debug 888); G06 releases 0.1.0 → 0.1.1 portable and installed; UP8–UP13, IN2, IN3 |
| Windows 11 24H2 x64 | Supported | Installs per user, no prompt | NOT_RUN (owner, on a 24H2 PC or VM: rows C1–C3 below) |
| Windows 11 26H1 x64 | Supported once released and tested | Installs per user, no prompt | NOT_RUN (not available yet) |
| Windows 11 23H2 Enterprise/Education x64 | Best effort (.NET 10 supports it; not tested) | Installs per user, no prompt | NOT_RUN (optional) |
| Windows 10 21H2 Enterprise LTSC / IoT Enterprise LTSC x64 | **Best effort** (.NET 10 supports it; not tested) | Windows 10 notice: "may work, not tested (best effort)", Install anyway? | NOT_RUN (optional, owner VM: C1–C3 plus the notice in C4) |
| Windows 10 22H2 Home/Pro x64 | **Not supported** (.NET 10 does not support it) | Windows 10 notice: "Home and Pro are not supported", Install anyway? | Not tested by design. C4 checks the notice. |
| Windows 10 1809 / 1607 Enterprise LTSC | Not supported in v1 | Refused: `MinVersion=10.0.19041` (Windows 10 2004 or later) | Not tested by design (ADR 0001 amendment) |
| Arm64, x86 | Not supported in v1 (backlog B09) | Refused: `ArchitecturesAllowed=x64compatible` | N/A |

The installer script is pinned by `InstallerScriptTests` (per-user, x64, `MinVersion`, Windows 10 notice that
`/SUPPRESSMSGBOXES` answers with Yes, so IT silent installs continue).

## 2. Environments

"Automated" means tests that run on every CI run and on the dev host. Rows marked "owner" need hardware or accounts
the agent does not have.

| # | Scenario | Steps | Expected | Automated evidence | Status |
|---|---|---|---|---|---|
| C1 | Clean install on a supported OS | Download the latest release's setup EXE and run it with defaults; then start Dynamic Island | No UAC prompt; the island appears at the top centre; tray icon present; Settings › About shows the version | IN1 (silent), IN2 | PASS on 25H2 (IN2/IN3, G06); other OS NOT_RUN |
| C2 | Portable copy | Unzip the portable ZIP to a folder you own; run `DynamicIsland.exe` | As C1, and nothing is written next to the EXE except updates | `AppLayoutTests`; UP8 | PASS on 25H2 (UP8) |
| C3 | Update and rollback | As UP8 and UP10 in `TEST_MATRIX.md` | Updates in place; a broken update rolls back | `UpdateSwapTests` | PASS on 25H2 (UP8, UP10) |
| C4 | Windows 10 notice | On Windows 10, run the setup EXE; then run it again with `/VERYSILENT /SUPPRESSMSGBOXES` | Interactive: the notice names the build, says Enterprise/IoT 21H2 is best effort and Home/Pro is not supported, default button **No**; No cancels, Yes installs. Silent: installs without a prompt | `InstallerScriptTests`; the script compiles (ISCC 6, 2026-10-29) | NOT_RUN (owner, Windows 10 VM) |
| C5 | Display scaling 100/125/150/200 % | G02 rows D1–D4 | Notch sized and placed per scale | `IslandPlacementCalculatorTests`, `NotchVisualTests` | 100 % PASS; others NOT_RUN (G02) |
| C6 | One and two monitors, unplug, rotate | G02 rows | Island follows the chosen monitor and falls back to the primary | `DisplayChangeTests.Island_falls_back_when_the_preferred_monitor_is_unplugged_and_returns_when_it_is_back`, `No_monitors_keeps_the_last_placement` | 1 monitor PASS; 2 monitors NOT_RUN (owner) |
| C7 | Laptop: battery and dock/undock | On a laptop: check the system tab with and without power; turn on Energy saver; dock and undock an external screen | Battery tile shown only with a battery; animations instant under Energy saver (G02 P2); island moves to the remaining screen | `MetricsIntegrationTests.Tiles_show_each_value_and_hide_battery_without_one` | Desktop without a battery: battery tile hidden (automated); laptop NOT_RUN (owner) |
| C8 | Remote Desktop | Connect to the PC over RDP while the island runs; open and close the island; disconnect and reconnect; also start the app inside an RDP session | Island visible in the session at the session's DPI; no crash on disconnect or reconnect; one instance per user | (single-instance names include the user SID) | NOT_RUN (owner) |
| C9 | Audio and no audio | Disable all playback devices (Sound settings › device › Disable) or use a PC with none; open the volume and media tabs; re-enable | The volume tab idles without errors; media idles; both recover when a device returns | `Without_windows_audio_it_idles`, `Without_media_sessions_it_idles_without_retrying`, `Core_audio_connects_and_reads_without_changing_anything` | Audio present PASS (dev host); no-audio NOT_RUN (owner, real device) |
| C10 | Network and offline | Turn on Airplane mode (or unplug); Settings › Updates › Check for updates; then go online and check again | Offline: "Couldn't reach GitHub. Check your internet connection and try again.", no crash; online: the result is shown | `GitHubReleaseSourceTests.Offline_untrusted_and_oversized_answers_never_throw` | Online PASS (UP1 live, 2026-10-28); offline NOT_RUN (owner) |
| C11 | Standard (non-admin) account | IN4 in `TEST_MATRIX.md` | No UAC prompt at any point; install, update, uninstall work | n/a | Admin account with a UAC-filtered token (the app runs non-elevated): PASS on 25H2; real standard account NOT_RUN (IN4) |
| C12 | Enterprise policy: sign-in start blocked | `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer` `DisableCurrentUserRun=1` (DWORD), then Settings › Start with Windows | Shown as blocked by policy; nothing written | `RegistryStartupRegistrationTests.Per_user_run_policy_is_reported`, `StartupPolicyTests.Policy_block_and_missing_launcher_write_nothing` | Automated PASS; manual NOT_RUN |
| C13 | Enterprise policy: self-update disabled | IN5 in `TEST_MATRIX.md` (`/MANAGED=1`; HKLM `DisableSelfUpdate=1`) | Settings › Updates says IT manages updates; no download offered | `UpdatePolicyTests.Machine_policy_wins_then_user_policy_then_the_installer_flag`, `An_it_managed_copy_*` | Automated PASS; manual NOT_RUN (IN5, needs admin) |
| C14 | Enterprise policy: AppLocker or WDAC blocks per-user apps | On a test VM with AppLocker rules that block `%LOCALAPPDATA%` executables, run the installed copy | The launcher says the files may be missing or blocked; no bypass is attempted | `Launcher/Program.cs` start-failure path | NOT_RUN (owner, VM) |
| C15 | Elevated copy and normal copy | Start Dynamic Island with "Run as administrator", then start it normally | The second start leaves quietly; no crash | `SingleInstanceGuardTests.A_running_copy_this_process_may_not_open_counts_as_the_running_instance` (P07-01) | Automated PASS; manual NOT_RUN |

## 3. Notes for README, installer and About (P07-04)

- Say "Windows 11 (64-bit)". Mention Windows 10 only as "Enterprise/IoT 21H2 LTSC: best effort, not tested;
  Home/Pro: not supported".
- Never promise SmartScreen or antivirus behaviour for unsigned builds (ADR 0037).
- The self-contained runtime is 10.0.12 on this host. Before any public release, bump the pinned SDK to the latest
  .NET 10 security patch (`AGENT_PROGRESS.md` › BLOCKERS).
