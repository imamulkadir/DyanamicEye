# 0037 — Update fallbacks: read-only folders, managed copies, cancel and disk space, optional code signing

- Status: Accepted
- Date: 2026-10-28
- Task: P06-08

## Context
Guide P06-08 asks for:
- a portable update fallback for folders the app can't write to;
- an IT-managed or manual mode;
- handling of a cancelled download and of disk-space failures;
- code signing as an optional path, with no claim that SmartScreen is guaranteed to accept the app.

What already existed:
- **ADR 0031:** the IT policy, and the `Managed` stage that shows only the release details.
- **ADR 0033:** `ManualOnly` (release page), and Cancel returns to the details.
- **ADR 0034:** a free-space pre-check, disk-full mapping, and a `.partial` file that is deleted on any failure.
- **ADR 0035:** `UpdateHandover` refuses a read-only layout. Before this change that refusal came **after** the
  download, as a failed install with a dead-end sentence.

## Decision
1. **Read-only folder: hand over the verified file, never elevate.**
   - `IUpdateInstaller.CanInstallInPlace` is checked when a download completes. It is a default interface member set
     to `true`. `LayoutUpdateInstaller` asks `UpdateHandover.CanUpdateInPlace`: a release payload whose layout root
     accepts a new file. The check creates and deletes a probe file.
   - When the check is `false`, the flow ends in the new stage **`ReadyToUnpack`**:
     - The window says the file was downloaded and checked, and that this copy's folder can't be changed.
     - It tells the user to close Dynamic Island and unzip the file into a folder they can change, or to ask whoever
       manages the PC.
     - **Show file** opens File Explorer with the verified ZIP selected in the per-user staging folder.
   - Install is never offered or attempted in this stage. No UAC prompt and no attempt to work around folder
     permissions (AGENTS.md: don't bypass application controls).
   - The check runs again after each download, so a folder that became writable updates in place next time.
2. **Managed and manual modes stay as they are.**
   - IT policy or `/MANAGED=1` → `Managed`: details only, with a support route.
   - No pinned key, a development build or no ZIP in the release → `ManualOnly`: the release page.
   - Both are covered by existing tests (`UpdateFlowTests`, `UpdateWindowTests`, UP4).
3. **Cancel and disk space.**
   - Cancel throws `OperationCanceledException`. The `.partial` file is deleted and the flow returns to the details.
     Tests cover this at both the downloader level and the window level.
   - Disk space is checked before any byte is requested: the package size plus 200 MB spare. A full disk during the
     write maps to the same plain sentence.
   - The free-space source can now be injected, so the pre-check is unit-tested.
4. **Code signing is optional.**
   - Provenance comes from the signed release manifest (ADR 0034). It costs nothing and needs no account.
   - Authenticode or Azure Trusted Signing may be added in P07. The docs must never promise that SmartScreen or
     antivirus will accept an unsigned or newly signed build. Reputation builds over time, and users may see
     "Windows protected your PC" on first run. README and install guides (P07-04) say so plainly.

## Consequences
- A portable copy in `Program Files` or another read-only folder still benefits from verified downloads. Replacing the
  copy is the user's own step.
- The staged ZIP stays in `%LOCALAPPDATA%\DynamicIsland\updates\<version>\` until the next download replaces it. Only
  one version is kept (ADR 0034).
- Manual check: test-matrix row UP11 now expects **Show file** for a read-only folder.
