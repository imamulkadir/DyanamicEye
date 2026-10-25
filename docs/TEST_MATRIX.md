# TEST_MATRIX.md — Manual Windows validation

Automated tests cover logic and what a single CI desktop can show. This file lists the **manual checks on real hardware**
that gates require. Record each run in `docs/AGENT_PROGRESS.md` → TEST EVIDENCE: the date, Windows version/build,
monitors (resolution @ scaling) and PASS/FAIL with a note. Status values: `NOT_RUN`, `PASS`, `FAIL`, `N/A`.

## How to run the app for a check
1. Build: `dotnet build DynamicIsland.sln -c Release`.
2. Start: `src\DynamicIsland.App\bin\Release\net10.0-windows\DynamicIsland.exe`. Use the Launcher next to it once packaging exists (P06).
3. Exit: run `taskkill /IM DynamicIsland.exe` (without `/F`). It sends a normal close request, so the app exits cleanly with code 0. Clicking the island and pressing Alt+F4 should also work but is not verified yet. The tray "Exit" arrives in P03.
4. Logs are in `%LOCALAPPDATA%\DynamicIsland\logs\dynamicisland-YYYYMMDD.log`. Each placement writes an `IslandPlaced` entry: primary monitor yes/no, fallback yes/no, scale, size in px. Monitor ids are never logged.

Expected size of the collapsed island: **200 × 36 px × the monitor's scale**, for example 300 × 54 px at 150 %. It sits **8 px × scale** below the top of the usable screen area and is centered horizontally.

## G02 — Island shell (P02-02 adds these; P02-03..05 extend them)

| # | Scenario | Steps | Expected | Status |
|---|---|---|---|---|
| D1 | Scaling 100 % | Settings → Display → Scale 100 %, start the app | Island top-center, 200×36 px, crisp edges | PASS (agent, 2026-10-25, 1920-px monitor) |
| D2 | Scaling 125 % | Set 125 %, start the app | 250×45 px, top-center, crisp (no blur) | NOT_RUN |
| D3 | Scaling 150 % | Set 150 %, start the app | 300×54 px, crisp | NOT_RUN |
| D4 | Scaling 200 % | Set 200 %, start the app | 400×72 px, crisp | NOT_RUN |
| D5 | Scaling changed while running | With the app open, switch 100 % → 150 % | Within ~1 s the island resizes to 300×54 px and stays centered; log shows `IslandPlaced` | NOT_RUN |
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
| H2 | Keyboard | Click the island, press Tab / Escape | Focus behaviour is defined in P02-03; for now the window must not trap focus | NOT_RUN |
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
