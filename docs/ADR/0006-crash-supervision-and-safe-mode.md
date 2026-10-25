# 0006 — Crash supervision, Safe Mode and test-only hooks

- Status: Accepted
- Date: 2026-10-25
- Task: P01-04

## Context
D03 allows at most 2 automatic restarts per 5 minutes, then Safe Mode or stop. Guide §4.5 says a child launcher tracks crash timestamps, an explicit Exit suppresses relaunch, and a critical launcher failure stops with manual recovery instructions. Guide §3 says the launcher must not become a long-running service "without a demonstrated need". P01-04 also requires injected-crash tests, and a crash switch must not be active in shipped builds.

## Decision
1. **Policy (`Core/Lifecycle/CrashPolicy`):** crashes are counted inside a rolling 5-minute window, ignoring timestamps in the future.
   - Crashes 1 and 2 → restart.
   - Crash 3 → restart once in Safe Mode.
   - A crash while in Safe Mode → stop.
2. **Supervisor (`Core/Lifecycle/AppSupervisor`)** is pure and tested with a scripted process runner. It acts on the exit codes from ADR 0005:
   - **0** (Exit, sign-out, second instance) → finish, never relaunch.
   - **3** (Restart) → relaunch normally without counting a crash. More than 5 restart requests per launcher lifetime count as crashes.
   - **Anything else**, including abnormal termination codes → record a crash and apply the policy.
3. **Launcher (`DynamicIsland.Launcher.exe`, WinExe):**
   - It starts `DynamicIsland.exe` from its own folder and passes only the allow-listed `--safe-mode`.
   - It writes its own `launcher-*.log`; a shared file could interleave with the app's.
   - Crash history goes to `data/crash-history.json`, as atomic small JSON. If the file is corrupt or can't be written, the history continues in memory and is never lost.
   - On stop, it shows a native message with recovery steps and the log folder.
   - **Lifetime:** it runs exactly as long as the app. Crash supervision is the demonstrated need; it is not a service and does not stay running after the app exits cleanly.
   - **Dependencies:** it may reference Core and Infrastructure only. The architecture test enforces this.
4. **Entry point:** sign-in startup (P03) and shortcuts target the **launcher**. Launching `DynamicIsland.exe` directly still works, just without automatic restart.
5. **Safe Mode in the app:** `--safe-mode` is logged, and "(Safe Mode)" is added to the window title. No optional components exist yet; from P04 on, widgets and their providers must check `LaunchArguments.SafeMode` and not start. All other arguments are counted, logged and ignored.
6. **Test-only hooks (`Core/Diagnostics/TestHooks`):**
   - `DYNAMICISLAND_TEST_CRASH` (`ui`/`background`/`startup`), `DYNAMICISLAND_TEST_DATA_ROOT` and `DYNAMICISLAND_TEST_NO_DIALOGS` are honoured **only when `DYNAMICISLAND_TEST_HOOKS` is compiled in, which happens in Debug builds only**. In Release, `TestHooks.Get` always returns `null`.
   - A Release-only smoke test proves that the crash and data-root variables are ignored.
   - CI builds and tests both Release and Debug.

## Alternatives considered
- **In-process restart** (the app restarts itself): rejected, because a crashed process can't reliably supervise itself.
- **Windows Task Scheduler or a service for restarts:** rejected. These are prohibited or elevation-prone, and need a guide change.
- **A crash switch available in Release behind an "obscure" variable:** rejected, because shipped binaries must not be steerable into failure.

## Consequences
- Release packaging (P06) must ship the launcher next to the app, and installer and portable shortcuts point to the launcher.
- The tray "Restart" (P03) exits with code 3. Leaving Safe Mode is a restart without `--safe-mode`.
- Crash history is local, has no personal data, and is capped at 10 entries.
