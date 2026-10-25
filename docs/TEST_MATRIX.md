# TEST_MATRIX.md — Manual Windows validation

Automated tests cover logic and what a single CI desktop can show. This file lists the **manual checks on real hardware**
that gates require. Record each run in `docs/AGENT_PROGRESS.md` → TEST EVIDENCE: the date, Windows version/build,
monitors (resolution @ scaling) and PASS/FAIL with a note. Status values: `NOT_RUN`, `PASS`, `FAIL`, `N/A`.

## How to run the app for a check
1. Build: `dotnet build DynamicIsland.sln -c Release`.
2. Start: `src\DynamicIsland.App\bin\Release\net10.0-windows\DynamicIsland.exe`. Use the Launcher next to it once packaging exists (P06).
3. Exit: run `taskkill /IM DynamicIsland.exe` (without `/F`). It sends a normal close request, so the app exits cleanly with code 0. Clicking the island and pressing Alt+F4 should also work but is not verified yet. The tray "Exit" arrives in P03.
4. Logs are in `%LOCALAPPDATA%\DynamicIsland\logs\dynamicisland-YYYYMMDD.log`. Each placement writes an `IslandPlaced` entry: primary monitor yes/no, fallback yes/no, scale, size in px. Monitor ids are never logged.

The island is drawn as a **notch attached to the top of the usable screen area** (flat top, small curved shoulders, rounded bottom corners, black), with a speaker slit and a front-camera lens inside. Collapsed size: **236 × 32 px × the monitor's scale**, for example 354 × 48 px at 150 %. It is centered horizontally with no gap at the top. The (transparent) window is always 420 × 180 px × scale.

## G02 — Island shell (P02-02 adds these; P02-03..05 extend them)

| # | Scenario | Steps | Expected | Status |
|---|---|---|---|---|
| D1 | Scaling 100 % | Settings → Display → Scale 100 %, start the app | Notch top-center, flush with the top edge, 236×32 px, crisp edges | PASS (agent, 2026-10-25, 1920-px monitor, screenshot) |
| D2 | Scaling 125 % | Set 125 %, start the app | Notch 295×40 px, flush top, crisp (no blur) | NOT_RUN |
| D3 | Scaling 150 % | Set 150 %, start the app | Notch 354×48 px, crisp | NOT_RUN |
| D4 | Scaling 200 % | Set 200 %, start the app | Notch 472×64 px, crisp | NOT_RUN |
| D5 | Scaling changed while running | With the app open, switch 100 % → 150 % | Within ~1 s the notch resizes to 354×48 px and stays centered; log shows `IslandPlaced` | NOT_RUN |
| M1 | Two monitors, primary | Two monitors, start the app | Island on the **primary** monitor | NOT_RUN |
| M2 | Mixed DPI | Primary 100 %, secondary 150 %, make the secondary primary in Settings | Island moves to the new primary at 150 % size, crisp | NOT_RUN |
| M3 | Unplug the island's monitor | Island on a monitor, unplug that monitor (or disable it in Display settings) | Island appears on the remaining monitor within ~1 s; log `fallback True` only if a preferred monitor was set | NOT_RUN |
| M4 | Re-plug | Reconnect the monitor | With a preferred monitor set (possible from P03), the island returns to it; otherwise it stays on primary | NOT_RUN |
| M5 | Rotate | Change the monitor's orientation to portrait | Island top-center of the rotated monitor | NOT_RUN |
| M6 | Resolution change | Change the resolution (e.g. 1920×1080 → 1280×720) | Island re-centered at the new width | NOT_RUN |
| T1 | Taskbar at top | Taskbar settings → move to top (Windows 10 or a third-party tool on 11), or use an app bar | Island sits **below** the taskbar, never covering it | NOT_RUN |
| T2 | Taskbar auto-hide | Turn on auto-hide | Island stays at the top of the full screen; nothing jumps when the taskbar slides in | NOT_RUN |
| S1 | Sleep / wake | Sleep the PC, wake it | Island still visible at the correct place; no duplicate; log shows a placement after resume | NOT_RUN |
| S2 | Lock / unlock | Win+L, unlock | Island unchanged | NOT_RUN |
| H1 | Click-through corners | Put a window under the island's rounded corner and click exactly at the corner | The window below receives the click (the automated smoke test checks this too) | PASS (automated, 100 %) |
| H2 | Keyboard | Click the island, press Escape; click it, press Tab to the pin button, press Space; press Ctrl+P | Escape collapses and returns focus to the previous app; Tab reaches the pin button (visible focus ring); Space and Ctrl+P toggle the pin | NOT_RUN |
| H3 | Hover preview | Rest the mouse on the collapsed notch about 0.3 s, then move away | It grows slightly (264×38 at 100 %) after about 1/4 s, and shrinks back immediately on leaving; brushing past quickly does nothing | NOT_RUN |
| H4 | Click expand / collapse | Click the island; move the mouse away; click again, then press Escape | Click expands it downward from the top edge (420×180 px at 100 %; sensor row stays at the top); moving away collapses it unless pinned; Escape collapses it | NOT_RUN |
| H5 | Pin | Expand, click the pin, move the mouse away | It stays expanded with a green pin; Escape or the pin again collapses it | NOT_RUN |
| H6 | Click-through while expanded | Expand the island and click just outside its rounded edge | The window below receives the click | NOT_RUN |
| H7 | Narrator | Turn on Narrator (Ctrl+Win+Enter), Tab or click to the island | Narrator reads "Dynamic Island, collapsed" and offers expand; after expanding it reads "expanded"; the pin is announced as a toggle | NOT_RUN |
| H8 | Reduced motion | Settings → Accessibility → Visual effects → Animation effects **off**; expand and collapse | Size changes instantly, with no animation | NOT_RUN |
| H9 | Expanded screenshots | — | Notch design captured by the agent 2026-10-25: collapsed notch flush with the top edge (speaker slit, camera lens, sensor dot); expanded notch with title, placeholder and pin | PASS (agent, 100 %) |
| F1 | Fullscreen video | Play a video fullscreen (browser or player) on the island's monitor | The island disappears within about 0.3 s and comes back the same way (pinned stays pinned) when you leave fullscreen | NOT_RUN |
| F2 | Browser F11 | Press F11 in Edge or Chrome, then F11 again | Hides, then returns | NOT_RUN |
| F3 | Borderless game | Run a game in borderless/windowed-fullscreen | Hides while the game is in front; returns when you alt-tab to the desktop | NOT_RUN |
| F4 | Exclusive fullscreen game | Run a game in exclusive fullscreen | The island is hidden or simply not drawn over the game; it never flickers on top; it returns after quitting | NOT_RUN |
| F5 | Presentation | PowerPoint slide show | Hides during the slide show | NOT_RUN |
| F6 | Other monitor | Two monitors: fullscreen video on the monitor **without** the island | The island stays visible | NOT_RUN |
| F7 | Maximized ≠ fullscreen | Maximize a normal window (also with the taskbar on auto-hide) | The island stays visible | NOT_RUN |
| F8 | Exception | Add the player's exe (e.g. `vlc.exe`) to `behavior.fullscreenExceptions` in `%LOCALAPPDATA%\DynamicIsland\data\settings.json`, restart, play fullscreen | The island stays visible (the settings UI comes in P03) | NOT_RUN |
| F9 | Automated end to end | Smoke `Island_hides_while_a_fullscreen_window_is_in_front_and_returns_afterwards` | Hide and return | PASS (agent, Release ×2); SKIPPED in Debug (Windows refused foreground to the test window) |
| I1 | App icon | Explorer: view `DynamicIsland.exe` with small, medium, large and extra-large icons at 100 % and 200 % | The original dark-tile capsule icon, crisp at every size | NOT_RUN |
| A1 | Not in taskbar / Alt+Tab | Start the app, look at the taskbar, press Alt+Tab | No taskbar button and no Alt+Tab entry (the automated smoke test checks the style) | NOT_RUN |

## G01 — Reliability (all automated; recorded for completeness)
| # | Scenario | Status |
|---|---|---|
| R1 | Corrupt settings recover | PASS (automated + manual run 2026-10-25) |
| R2 | Widget failure isolated | PASS (automated) |
| R3 | Exit never auto-relaunches | PASS (automated, real launcher) |
| R4 | Third crash does not loop | PASS (automated end-to-end in Debug) |
| R5 | No duplicate app | PASS (automated) |
