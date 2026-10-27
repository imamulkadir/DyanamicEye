# 0031 — Per-user installer (Inno Setup) and the IT-managed update policy

- Status: Accepted
- Date: 2026-10-27
- Task: P06-02

## Context
- Guide P06-02: per-user installer, no elevation, Start menu, uninstall cleanup without deleting user data unless
  explicitly requested; an IT-managed flag disables self-update; test with a Standard User account. §2: evaluate WiX
  per-user vs Inno Setup per-user (license, security, enterprise), no HKLM writes. §7 asset name
  `DynamicIsland-Setup-win-x64.exe`; managed deployments show details and the IT support route, never self-install.
- Installs the release layout of ADR 0030.
- Owner decision (2026-10-27, asked with WiX MSI and "no installer" as alternatives): **Inno Setup**.

## Decision
1. **Inno Setup 6** (`packaging/installer/DynamicIsland.iss`), built by `tools/Publish-Layout.ps1 -Installer`
   (6.7.0 on the dev PC, 6.7.1 in the GitHub `windows-2025` image; CI builds it as a verification step). Permissive
   license (copyright notice and web addresses must stay in the binary).
2. **Per-user, never elevated:** `PrivilegesRequired=lowest`; installs to `{autopf}\DynamicIsland` =
   `%LOCALAPPDATA%\Programs\DynamicIsland`; uninstall entry and Start menu shortcut under the user (HKCU / user Start
   menu); optional desktop icon (off by default); x64, Windows 10 19041 or later (ADR 0001 support levels apply).
3. **Upgrades and reinstalls** replace the payload: older `app-*` folders are deleted, `current.json` is rewritten
   (in-app updates keep the previous version for rollback instead, P06-06).
4. **Running copy:** before installing and before uninstalling, Setup posts `WM_CLOSE` to the island's tray window
   (`DynamicIsland.Tray`, which means tray › Exit) and waits up to 15 s. Restart Manager alone did not help: the
   uninstaller does not close apps, and the windowless launcher cannot be closed by it. Found by the install test:
   uninstalling a running copy left it running on a half-deleted folder, where it crashed on exit and was relaunched
   by the crash supervisor.
5. **Uninstall** removes the program, shortcuts, uninstall entry and our sign-in `Run` value (only if it points into
   the install folder). **User data in `%LOCALAPPDATA%\DynamicIsland` is kept** unless the user answers Yes to "Also
   delete your settings, notes, tasks and logs?" (default No); a silent uninstall never deletes it.
6. **IT-managed update policy** (`Core/Distribution/UpdatePolicy`, read once at start by
   `Platform.Windows/Startup/UpdatePolicyReader`):
   - `HKLM\Software\Policies\DynamicIsland\DisableSelfUpdate` (DWORD) — Group Policy, Intune, scripts; wins;
   - `HKCU\Software\Policies\DynamicIsland\DisableSelfUpdate` (DWORD) — user policy;
   - `managed.json` next to the root launcher, written by `Setup.exe /MANAGED=1` (e.g. `/VERYSILENT /MANAGED=1`).
   A value of 0 at a higher level explicitly allows updates; a present but unreadable `managed.json` counts as
   managed (fail closed). Settings › Updates then says who manages updates ("your organization's policy" or
   "whoever installed this copy") and locks the daily-check option. P06-03/04 must honor it (no checks, no
   downloads, no installs).

## Alternatives considered
- WiX MSI (per-user) — offered; the owner chose Inno Setup. MSI would suit Group Policy deployment, but per-user MSIs
  have quirks and WiX v6+ is under the Open Source Maintenance Fee terms.
- MSIX — needs a trusted signing certificate or developer mode (ADR 0030).
- Deleting user data on uninstall by default — rejected by the guide (data loss).

## Consequences
- Installer 49 MB (LZMA2), portable ZIP 69 MB, both from the same layout.
- Not signed: SmartScreen may warn on first download until a signing decision (P07); no bypass is promised.
- Standard User account testing is a manual row (IN4): the dev account runs at Medium integrity (not elevated), which
  already proves no elevation is requested, but a real Standard User test is still owed.
- Manual rows IN1–IN5 in `docs/TEST_MATRIX.md`.
