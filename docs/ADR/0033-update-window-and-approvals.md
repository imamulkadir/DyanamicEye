# 0033 — Update window: release details, separate Download and Install approvals

- Status: Accepted
- Date: 2026-10-27
- Task: P06-04

## Context
- Guide P06-04: version, release date, formatted download size, notes sections (features/fixes), link,
  progress/cancel, retry; **separate download approval and install approval**; never install silently.
- Guide §7: notification → release details → **Download** approval → download/verify → **Install** approval → postpone
  or install → health check → commit/rollback. IT-managed copies show details and the support route only. Never invent
  sizes.
- Downloading and verifying are P06-05, the swap P06-06. P06-04 builds the steps and the window and stops at the
  interfaces.
- Release notes are untrusted (ADR 0032).

## Decision
1. **Steps** (`Core/Updates/UpdateFlow`), one per release:
   - The steps are Review → (Download) → Downloading → ReadyToInstall → (Install) → Installing.
   - Each arrow in brackets runs only when the user selects that button.
   - **Cancel** returns to Review.
   - A failure moves to Failed with a plain sentence. **Retry** repeats only the step that failed, as a fresh approval.
     A failed install never downloads again.
   - **Install** is ignored until a package is downloaded.
   - Other end states:
     - **Managed** (IT policy): details only, no download.
     - **ManualOnly**: used when the release has no file for this copy, or this build has no in-app downloader. Only
       the release page is offered.
   - Progress comes from `IUpdateDownloader` as bytes received. The total is GitHub's asset size.
   - `IUpdateDownloader` and `IUpdateInstaller` are the seams for P06-05 and P06-06.
2. **Which file** (`UpdatePackages`):
   - An installed copy (Inno's `unins000.exe` at the layout root) uses `DynamicIsland-Setup-win-x64.exe`.
   - A portable copy uses `DynamicIsland-Portable-win-x64.zip`.
   - Matching is by exact name (guide §7 names).
   - Sizes are 1024-based, like File Explorer ("48.6 MB").
3. **Notes** (`ReleaseNotes`):
   - Markdown headings become sections ("Features", "Fixes", …). List items, wrapped lines and paragraphs become plain
     lines.
   - Inline Markdown is reduced to its words. Links keep their text but never their address, and images are dropped.
   - HTML stays visible as text.
   - Limits: 12 sections, 40 items each, 500 characters each.
   - Only the release page link, already validated as `https://github.com/<repo>/releases/tag/…`, is clickable.
4. **Window** (`App/UpdateWindow`):
   - Content: the heading "Dynamic Island X is available"; the installed version, release date and download size; the
     notes; "View this release on GitHub"; a progress bar with "12.2 MB of 48.6 MB (25%)"; a polite live status line.
   - Two buttons change with the step: Download/Not now, Cancel download, Install now/Later, Retry/Close, Open release
     page/Close.
   - No button is the default and focus starts on the notes, so a stray Enter never approves anything. Esc is Not
     now/Cancel/Later.
   - Closing the window cancels a running download.
   - The steps outlive the window (`UpdateWindowService`): after **Later**, reopening goes straight to Install now. A
     newer release starts over.
5. **Notification**:
   - When an automatic check finds a newer version, the tray shows one Windows notification ("Version X is available.
     Select to see what's new."). It uses `NIIF_RESPECT_QUIET_TIME`, so Windows' Do not disturb and notification settings
     apply.
   - Each version is announced **once**: `notifiedVersion` in `update-check.json` survives restarts.
   - Selecting the notification opens the update window.
   - While an update is known, the tray item reads "Update available (X)…" and opens the window. Settings › Updates shows
     **View update…**.
   - A cached answer after a restart counts too (`UpdateMessages.Newer`).
6. **This build:** no downloader or installer is registered yet, so the window offers the release page (ManualOnly).
   P06-05 and P06-06 register them, and then Download and Install appear without changing the UI. No release exists
   before P06-09.

## Alternatives considered
- **One "Update" button that downloads and installs.** Rejected because the guide requires separate approvals.
- **WinRT toast (AppNotificationManager).** It needs app identity/AUMID registration. The shell notification from the
  existing tray icon is enough and respects quiet time.
- **Rendering the notes as Markdown or HTML** (WebView or FlowDocument with links). Rejected because the notes are
  untrusted. Plain text is safe and readable.
- **Notifying on every check.** Rejected as nagging. Once per version, then the tray item and Settings keep it visible.

## Consequences
- Manual rows UP5–UP7 in `docs/TEST_MATRIX.md`.
- P06-05 implements `IUpdateDownloader` (staging, size, checksum, provenance) and P06-06 implements `IUpdateInstaller`.

## Manual test (UP5)
A fake cached release, used without any network: the app treats it as an answer GitHub gave just now (not due again
for a day). Set both times to the current UTC time.
```json
{"schemaVersion":1,"lastAttemptUtc":"2026-10-27T09:00:00Z","lastSuccessUtc":"2026-10-27T09:00:00Z",
 "consecutiveFailures":0,"retryAfterUtc":null,"etag":null,"noReleases":false,
 "latest":{"version":"9.9.9","tag":"v9.9.9","title":"Dynamic Island 9.9.9","publishedUtc":"2026-10-27T00:00:00Z",
  "pageUrl":"https://github.com/imamulkadir/DyanamicEye/releases",
  "notes":"## Features\n- **Test** feature with a [link](https://example.com)\n## Fixes\n- A fix","assets":[]}}
```
