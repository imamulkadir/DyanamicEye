# 0030 — Release layout: stable launcher and immutable versioned payloads

- Status: Accepted
- Date: 2026-10-27
- Task: P06-01

## Context
- Guide P06-01 / §2 packaging note: select the install and portable layout by proof of concept; immutable versioned
  payload and a stable launch path where practical; a single self-contained EXE alone does not give reliable rollback.
  Document executable moves, read-only folders, antivirus and company-policy failure modes.
- D01: portable EXE and per-user installer, neither needing admin rights. D12: a failed update must restore the
  previous working version and protect data. P06-06 will swap versions out of process and never overwrite an
  executable in use.
- Today `DynamicIsland.Launcher.exe` (crash supervisor, ADR 0006) starts `DynamicIsland.exe` from its own folder.
- Owner decision (2026-10-27): the portable version keeps its data in `%LOCALAPPDATA%\DynamicIsland`, like the
  installed one; moving data between PCs uses Export backup (ADR 0024).

## Decision
1. **Layout** (portable ZIP content = install folder content):
   ```
   DynamicIsland\
     DynamicIsland.exe            launcher — the stable path for shortcuts, the startup entry and restarts
     current.json                 { "current": "1.3.0", "previous": "1.2.0" }
     app-1.3.0\DynamicIsland.exe  self-contained app payload; never changed after it is written
     LICENSE.txt, THIRD_PARTY_NOTICES.md
   ```
   Installed: `%LOCALAPPDATA%\Programs\DynamicIsland` (P06-02). Data never lives in the layout.
2. **Launcher** (`AppLayout.ResolveApp`): with `current.json`, runs `app-<current>`; if that payload is missing, runs
   `app-<previous>` and logs `UsingPreviousVersion`; a broken or oversized pointer counts as missing. Versions must be
   SemVer (also keeps folder names free of path characters). Without `current.json` (development and tests) it runs
   the app next to it, never itself.
3. **App** (`AppLayout.LauncherFor`): running from `app-<version>` under a root with `current.json` and
   `DynamicIsland.exe`, it registers that root launcher for sign-in and uses it for restarts — so an update that
   replaces the payload never breaks the startup entry. Otherwise `DynamicIsland.Launcher.exe` next to it.
4. **Launcher build:** NativeAOT, one 3.6 MB file, no runtime and nothing extracted to `%TEMP%` at start (AV and
   AppLocker rules often block that). Proof of concept compared: untrimmed self-contained single file 39 MB; trimmed
   single file 14.3 MB (trimming only scoped to the launcher project); NativeAOT 3.7 MB, no warnings. Publishing needs
   the C++ build tools (CI runners have them); normal builds and tests do not.
5. **Payload build:** self-contained win-x64 (no .NET install, no admin), not single-file (single-file WPF extracts
   native files and does not help rollback). 159 MB on disk, portable ZIP 69 MB. `tools/Publish-Layout.ps1` builds the
   layout, the ZIP and its SHA-256; CI runs it as a verification step (nothing is uploaded or released — P06-09).
6. **Restore:** all projects list `RuntimeIdentifiers=win-x64` so CI's locked restore also covers `publish -r win-x64`.

## Failure modes (documented; later tasks noted)
- **Folder moved or renamed** (portable): the launcher resolves everything relative to itself, so the copy runs. The
  startup entry still points at the old path: the app repairs it on its next start when start-with-Windows is on
  (StartupPolicy, ADR 0015), and the Settings page shows "not available" until then. Proven by the PoC (moved copy).
- **Read-only folder** (e.g. extracted to Program Files, a locked-down share, or ACLs denying writes): the app runs —
  nothing is written to the layout. Updates cannot be applied there: P06-08 falls back to "download and replace
  manually". Proven by the PoC (create and write denied).
- **Antivirus** quarantines or blocks a payload file: the launcher falls back to the previous payload if one is kept,
  otherwise it shows "could not be started… files may be missing or blocked" with the logs folder. An unsigned new EXE
  may be held for SmartScreen/AV reputation checks on first run; no signing or bypass is promised (guide §2).
- **Company policy** (AppLocker/WDAC often blocks executables under `%LOCALAPPDATA%` or `%TEMP%`): the installed
  location may be blocked; the launcher then cannot start (`AppStartFailed` with the Win32 error is logged) and the
  user sees the same recovery message. Remedies: IT allows the publisher/path, or the portable layout in an allowed
  folder. We never try to work around such policies (AGENTS.md).
- **Lost `current.json`** in a release layout: the launcher finds no app (it never starts itself) and shows the recovery
  message; reinstalling or re-extracting fixes it. P06-06 writes the pointer atomically.

## Alternatives considered
- One self-contained single-file `DynamicIsland.exe` — rejected (guide §2): no rollback, self-extraction, in-use file.
- MSIX — rejected for now: sideloading needs a trusted certificate or developer mode; revisit with signing (P07).
- Framework-dependent payload with a shared private runtime (`AppHostDotNetSearch`) — smaller updates, but runtime
  patches would need their own versioned folders and launcher rebuilds; revisit if update size becomes a problem.

## Consequences
- Process list shows two `DynamicIsland.exe` (launcher and app), like other apps with this layout.
- Manual rows LY1–LY4 in `docs/TEST_MATRIX.md`; automated: `AppLayoutTests`, `LayoutSmokeTests`, CI packaging step.
- Open license item for the Windows SDK projection DLL before the first public release (THIRD_PARTY_NOTICES.md).
