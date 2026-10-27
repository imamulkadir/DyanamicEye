# 0035 — Updater: payload swap, health handshake and rollback

- Status: Accepted
- Date: 2026-10-27
- Task: P06-06

## Context
Guide P06-06 defines the updater protocol:
- A separate updater: prepare, exit gracefully, wait for the process, swap to the new version, launch, a health handshake
  within bounded time, then commit.
- A failure triggers rollback, and the old version stays intact until health is confirmed.
- An in-use executable is never overwritten.

Inputs from earlier tasks:
- **ADR 0030:** the release layout is `DynamicIsland.exe` (launcher) + `current.json` + immutable `app-<version>\` payloads,
  and the launcher already falls back to `previous`.
- **ADR 0034:** a verified, staged package.
- **ADR 0033:** the `IUpdateInstaller` seam.
- **ADR 0031:** the per-user installer. Its `[InstallDelete]` removes old payloads and its rollback ends when setup
  finishes, so re-running setup cannot keep the old version until the new one proves healthy.

## Decision
1. **One protocol for portable and installed copies: swap the payload from the verified portable ZIP.** The installed
   folder has the same layout (ADR 0030), so both copies download `DynamicIsland-Portable-win-x64.zip`. This
   **supersedes the ADR 0033 package choice for in-app updates**; the setup EXE remains for first installs and manual
   upgrades. For installed copies the updater also updates `DisplayVersion` in the per-user uninstall key (HKCU, no
   elevation). The installer's `[UninstallDelete]` removes every `app-*` folder so updater-created payloads never
   outlive an uninstall.
2. **Hand-over (app side, `IUpdateInstaller`):**
   - The app checks that it runs from a release layout and that the layout root is writable. If not, it shows a plain
     sentence and the release page (P06-08 adds the fuller portable fallback).
   - It writes `updates\update-request.json` (version, staged path, size, SHA-256, layout root, app and launcher process
     ids), starts `DynamicIsland.Updater.exe --request <path>` **from its own payload folder** (that folder becomes
     `previous` and is never deleted by this update), and exits normally (code 0), so the launcher finishes too.
3. **Updater steps** (`Infrastructure/Updates/UpdateSwap`; every step bounded and logged without paths):
   1. **Validate the request.** It must sit under the per-user `updates` folder, the version must be SemVer, the
      package must sit in that version's staging folder, and the layout must contain `current.json` and the launcher.
   2. **Wait for the app and launcher to exit** (30 s). If they don't, give up and change nothing ("close Dynamic
      Island and try again").
   3. **Re-verify the package size and SHA-256** (time-of-check/time-of-use).
   4. **Extract** only `app-<version>/…` and the root files into `app-<version>.partial`, with traversal, count and size
      guards, then rename the folder to `app-<version>`. Root files (launcher, licences) are replaced via
      temp + `File.Replace` with a `.bak` copy. That is allowed because the launcher has exited.
   5. **Point at the new version:** write `current.json` atomically as `{current: new, previous: old}` and write
      `updates\awaiting-health.json` naming the new version.
   6. **Launch** the root launcher, then wait up to **60 s** for `updates\health-<version>.ack`. The new app writes it
      once its window is shown and settings are loaded, and only if `awaiting-health.json` names its own version.
   7. **Commit:** delete the `.bak` copies, delete payloads other than current and previous, delete the staged package
      and the request, and update the uninstall `DisplayVersion` (installed copies).
   8. **Rollback** on any failure after step 4: stop the processes started in step 6 (process tree), restore
      `current.json` to the old pointer, restore the root files from `.bak`, delete `app-<new>`, start the old version,
      and write `updates\last-update-failed.json` (version + a short reason code). The app shows that once as a notice.
4. **Never** overwrite a running executable, delete the payload that launched the updater, or run anything from the
   release body. The updater itself runs from the old payload.
5. **Dependencies:** the updater references Core and Infrastructure only (architecture test). It logs to
   `updater-*.log` through the same redacting logger.
6. **App-side details** (`Infrastructure/Updates/UpdateHandover`, `App/LayoutUpdateInstaller`):
   - The launcher passes its process id to the app in `DYNAMICISLAND_LAUNCHER_PID`, next to `DYNAMICISLAND_SUPERVISED`.
   - The app takes the version for the health acknowledgment from its payload folder name (`app-<version>`), which is
     exactly the name the updater created. It does not use the assembly version.
   - The rollback note becomes one Windows notification ("Version x didn't start correctly, so the previous version was
     restored"). The note file is then deleted.
   - `IUpdateInstaller` is registered only when a signing key is pinned and the app runs from a release payload.
     Otherwise the window keeps offering the release page.
   - The uninstaller also removes `current.json`, which the updater rewrites.

## Consequences
- In-app updates are possible only when a release signing key is pinned (ADR 0034) **and** the copy runs from a
  release layout. Development builds keep the release page.
- G06 scenarios covered by tests: valid update (commit), crash or no health acknowledgment (rollback), locked or
  still-running app (abort without changes), bad checksum (abort), corrupt or traversing archive (abort), and a clean
  restart afterwards.
- P06-07 adds the pre-update data snapshot and the schema checks around the swap.
