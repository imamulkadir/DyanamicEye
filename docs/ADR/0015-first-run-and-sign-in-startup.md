# 0015 — First-run onboarding and sign-in startup

- Status: Accepted
- Date: 2026-10-26
- Task: P03-04

## Context
- D02 and guide §4.4: ask on first launch; per-user sign-in startup only. HKCU Run or a current-user Startup shortcut;
  no service, elevated scheduled task or HKLM. Start collapsed after an app-side delay of about 5 s. Keep a stable
  launcher path across updates, repair a stale registration when a portable copy is moved, clear it on uninstall.
- Guide P03-04: onboarding asks for sign-in startup permission and shows privacy and widget choices; disabled startup
  is obeyed. AGENTS.md: never bypass corporate application controls.
- Settings already hold the consents `GeneralSettings.StartWithWindows` and `UpdateSettings.AutomaticChecks`
  (`NotAsked` / `Granted` / `Declined`, ADR 0003). ADR 0014 showed them read-only until this task.
- Several copies can share one profile: an installed copy, a moved portable copy, the build output used by tests.

## Decision
1. **Mechanism: the HKCU Run key only.** Value `DynamicIsland` under
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, data `"<folder>\DynamicIsland.Launcher.exe" --startup`. No
   Startup-folder shortcut fallback: if Run is blocked we report it instead of trying another way.
   - **Obeying Windows:** if Settings › Apps › Startup or Task Manager turned the entry off
     (`Explorer\StartupApproved\Run`, odd first byte), we never change that value; the settings window says where to turn
     it back on.
   - **Obeying policy:** if `DisableCurrentUserRun` is set (HKCU or HKLM `...\Policies\Explorer`), we write nothing and
     report "blocked by your organization".
   - Turning startup off deletes our Run value only; the StartupApproved value is left alone so a user's earlier
     "disabled" choice survives.
2. **Delay and start mode.** The launcher, started with `--startup`, waits **5 s** (`LauncherContract.StartupDelay`)
   before its first app start and passes `--startup` to that first start only (crash restarts do not carry it). The app
   starts collapsed as always and never shows onboarding or activates a window on a `--startup` launch.
3. **Disabled startup is obeyed at run time too.** A `--startup` launch whose settings do not say `Granted` removes the
   stale Run value and exits cleanly (exit code 0, so the launcher does not relaunch). A `--startup` launch that finds
   another instance running exits quietly without bringing it to the front.
4. **Reconcile on every start and on every consent change** (`Core/Startup/StartupPolicy.Decide`, pure and unit-tested):
   - Consent not granted and a value exists → remove it.
   - Granted, policy block → nothing (reported).
   - Granted, no value → register this copy.
   - Granted, value points to another copy:
     - the user just switched startup on in this copy → register this copy;
     - otherwise only when the registered launcher **no longer exists** (moved or deleted portable copy) → repair.
       A different copy that still exists keeps the entry, so running tests or a second copy never steals it.
   - Without `DynamicIsland.Launcher.exe` next to the app, startup cannot be registered (reported).
5. **Onboarding window.** Shown after the island appears when either consent is `NotAsked`, except in Safe Mode and on
   `--startup` launches. It is owned by the island window and contains:
   - "Start Dynamic Island when I sign in to Windows" — unchecked by default;
   - "Check GitHub for updates once a day" — unchecked by default; download and install still need separate approval
     (P06);
   - a short privacy summary (offline, no account, no telemetry, local redacted logs);
   - the widget choices (enabled widgets, default all on).
   **Continue** saves the shown choices: an unchecked box is an explicit `Declined`. Closing the window without
   Continue saves nothing, and onboarding appears again on the next start.
6. **Settings window** (amends ADR 0014 §6): General gets "Start when I sign in to Windows" and Updates gets "Check for
   updates once a day" as real check boxes, with the registration status shown under the startup box. A box the user
   did not touch keeps `NotAsked`; Reset still never changes consents.
7. **Testing isolation.** Debug builds honour `DYNAMICISLAND_TEST_REGISTRY_ROOT` (a key under HKCU that replaces
   `Software\Microsoft\Windows\CurrentVersion`), so tests never touch the real Run key. Release builds ignore it (ADR 0006
   test-hook rule); the repair rule in 4 keeps Release smoke runs from moving a real registration.
8. **Logs** record actions and states only (registered, removed, repaired, blocked, disabled in Windows, failed with the
   exception type), never paths.

## Alternatives considered
- Startup-folder shortcut — rejected: a second mechanism to repair and clean up, and using it when Run is disabled by
  policy would bypass an administrator's choice.
- Scheduled task with a delay — rejected: guide forbids tasks that need elevation, and a per-user task adds a COM
  dependency for a 5 s delay the launcher can do.
- Always re-pointing the Run value to the running copy — rejected: tests and second copies would hijack the user's
  startup entry.
- Pre-checked consent boxes — rejected: D02 requires asking; consent must be an explicit choice.

## Consequences
- P06 (installer) must keep the launcher at a stable per-user path and delete the Run value on uninstall.
- The StartupApproved data format (odd first byte = turned off) is not documented by Microsoft; it is how Settings and
  Task Manager behave on Windows 11 24H2/25H2. Manual row ST3 checks it on real hardware; if it changes, we only
  misreport the status, because we never write that value.
- The startup delay is a constant for now; making it a setting needs a schema change (ADR 0003) and is not planned.
- Manual rows ST1–ST7 in `docs/TEST_MATRIX.md` (real sign-in, Task Manager disable, policy, moved portable copy).
