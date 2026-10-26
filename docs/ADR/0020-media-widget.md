# 0020 — Media widget and WinRT access

- Status: Accepted
- Date: 2026-10-26
- Task: P04-03

## Context
- Guide P04-03: media widget via the global Windows media sessions; handle no session, several players, stale
  sessions and unsupported actions; load album art safely. Guide §3 contract `IMediaSessionService`.
- The API is WinRT `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` (Windows 10 1809+,
  UniversalApiContract v7). Microsoft's current guidance for .NET 6+ desktop apps is a Windows-version TFM, which adds
  the `Microsoft.Windows.SDK.NET.Ref` targeting pack ("Call Windows Runtime APIs in desktop apps", Microsoft Learn,
  updated 2026-10-03). ADR 0002 anticipated raising `Platform.Windows` to such a TFM for this task.
- Track titles and artists are personal data (AGENTS.md: never in logs or issue drafts).

## Decision
1. **TFM** `net10.0-windows10.0.19041.0` for `Platform.Windows`, `App` and the two Windows test projects (a project
   must target at least the TFM of what it references). `Core`, `Widgets`, `Infrastructure` stay `net10.0`;
   `Launcher` and `Updater` stay `net10.0-windows`. The pinned SDK (10.0.401) selects the targeting pack
   (`Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57), so builds stay reproducible; lock files only change their TFM key.
   Runtime support is unchanged (ADR 0001: Windows 11 24H2/25H2).
   - **Cost:** `Microsoft.Windows.SDK.NET.dll` (~24 MB) and `WinRT.Runtime.dll` ship with the app. P06 decides on
     trimming; until then the size is accepted.
   - Build output folders are now `bin\<config>\net10.0-windows10.0.19041.0\`.
2. **Adapter** `Platform.Windows/Media/WindowsMediaSessionService`: follows the session Windows calls current
   (`CurrentSessionChanged`, `SessionsChanged`) and its `MediaPropertiesChanged`/`PlaybackInfoChanged`; **no polling**.
   - **None / stale:** no session, a `Closed` session, or one without title and artist → "nothing playing".
   - **Several players:** the current one is shown; the others are counted ("· 1 more playing").
   - **Unsupported actions:** the player's `Controls` flags, which players set per track (Media Player turns Previous off
     on the first queued track and Next off on the last); such buttons are disabled with the tooltip "… (not available
     right now)".
   - Players vanish at any moment: WinRT errors (`COMException`, `InvalidOperationException`, access denied) mean
     "no session" or "command refused", never a widget crash.
   - Unpackaged desktop apps need no capability declaration for this API; verified by an integration test on Windows 11.
   - **Commands are retried briefly:** players can refuse a command they advertise (Media Player refused "next" for a
     moment after a track change in the owner-requested check). A refused command whose control is enabled is retried
     up to 5 times, 250 ms apart (about 1 s); unsupported commands are not retried.
3. **Widget** `Widgets/MediaWidget`: event-driven loop (50–60 ms settle for bursts), publishes an immutable
   `MediaSnapshot`; status Degraded `no-media` when nothing plays, `media-unavailable` when the API is missing (then it
   idles, no retry loop). Commands `previous`, `play-pause`, `next` via `IWidget.ExecuteAsync`
   (`WidgetRegistry.ExecuteAsync` only for running widgets).
4. **Album art:** read only when the track changes; skipped above **12 MB** (players can hand out uncompressed bitmaps: Media Player gives a 2.36 MB 768×768 BMP — the first 2 MB cap hid it; found in a real-player check on 2026-10-26); decoded in the App off the UI thread
   (`AlbumArtCache`, `DecodePixelWidth` 96, frozen, only the current image kept, undecodable images shown as a
   placeholder). No paths or names are logged.
5. **View** (`App/MediaView`): art, title, artist (left out when the file has no artist tag; the title then wraps to two
   lines) and three icon buttons with names, tooltips, AutomationIds
   `Media.<command>`, keyboard focus ring. Playback position/progress is not shown (it would need ticking).
6. **Space:** `IslandLayout.Expanded` grows from 420×180 to **420×212** to fit the clock and media widgets; while the
   expanded panel shows the large clock, the notch's top-row time is hidden so the time never appears twice. The
   expanded/pinned visual references were regenerated.
7. **Privacy:** title, artist and art stay in memory and on screen only; logs record widget status codes and
   exception types (ADR 0004, 0007).

## Alternatives considered
- Hand-written COM interop for the WinRT types — rejected: fragile and far more code than the supported projection.
- A separate `Platform.Windows.Media` project to keep the TFM change small — rejected: `App` must raise its TFM anyway.
- Polling the current session — rejected: event-driven is cheaper and immediate (ADR 0011).

## Consequences
- P06 packaging accounts for the WinRT projection assemblies (size, trimming, signing).
- Manual rows MD1–MD5 in `docs/TEST_MATRIX.md` (real players: browser, Spotify/VLC/Groove-style apps).
- 2026-10-26: every call into Windows media sessions is bounded by `CallTimeout` (5 s). On the dev PC
  `GlobalSystemMediaTransportControlsSessionManager.RequestAsync` stopped completing (no player running); without a
  bound the widget waited forever and the integration suite hung. A timeout now means "no media" (start: unavailable).
