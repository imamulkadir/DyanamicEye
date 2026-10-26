# TEST_MATRIX.md — Manual Windows validation

Automated tests cover logic and what a single CI desktop can show. This file lists the **manual checks on real hardware**
that gates require. Record each run in `docs/AGENT_PROGRESS.md` → TEST EVIDENCE: the date, Windows version/build,
monitors (resolution @ scaling) and PASS/FAIL with a note. Status values: `NOT_RUN`, `PASS`, `FAIL`, `N/A`.

## How to run the app for a check
1. Build: `dotnet build DynamicIsland.sln -c Release`.
2. Start: `src\DynamicIsland.App\bin\Release\net10.0-windows10.0.19041.0\DynamicIsland.exe` (folder name since ADR 0020). Use the Launcher next to it once packaging exists (P06).
3. Exit: run `taskkill /IM DynamicIsland.exe` (without `/F`). It sends a normal close request, so the app exits cleanly with code 0. Clicking the island and pressing Alt+F4 should also work but is not verified yet. The tray "Exit" arrives in P03.
4. Logs are in `%LOCALAPPDATA%\DynamicIsland\logs\dynamicisland-YYYYMMDD.log`. Each placement writes an `IslandPlaced` entry: primary monitor yes/no, fallback yes/no, scale, size in px. Monitor ids are never logged.

The island is drawn as a **notch attached to the top of the usable screen area** (flat top, small curved shoulders, rounded bottom corners, black), with a speaker slit and a front-camera lens inside. Collapsed size: **236 × 32 px × the monitor's scale**, for example 354 × 48 px at 150 %. It is centered horizontally with no gap at the top. The (transparent) window is always 420 × 180 px × scale.

## G02 — Island shell: sign-off checklist

**G02 PASS needs** (guide §5): 100/125/150/200 % DPI; 1 and 2 monitors; unplug/rotate; fullscreen; sleep/wake; normal mouse and keyboard control, all **verified on actual Windows**.
Automated coverage (CI + dev host): placement, click-through, UIA expand/pin/collapse, fullscreen hide/return, the idle CPU budget and notch visual regression all PASS. **Every row below marked NOT_RUN needs the owner on real hardware.** When they all PASS (or are N/A with a reason), P02-02..P02-05 become DONE and G02 PASS is recorded in `AGENT_PROGRESS.md`.

| # | Scenario | Steps | Expected | Status |
|---|---|---|---|---|
| P1 | Idle CPU | Automated smoke `Collapsed_idle_island_uses_almost_no_cpu` | Under 300 ms CPU in 10 s idle | PASS (agent: 0 ms, 94 MiB working set) |
| P2 | Battery saver | Laptop: turn on Battery/Energy saver, expand and collapse the notch | No size animation (instant) while the saver is on; animations return when it is off | NOT_RUN |
| V1 | Notch visual regression | Automated `NotchVisualTests` (4 states vs reference PNGs) | Within tolerance | PASS (agent) |

### Detailed rows

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

## G03 — Tray, settings, sign-in (P03; started 2026-10-25 with owner approval while G02 is open)

**G03 PASS needs** (guide §5): Explorer restart restores the tray icon; sign-in starts one collapsed island when opted in; the user can fully exit, change monitor and reset an off-screen position.

| # | Scenario | Steps | Expected | Status |
|---|---|---|---|---|
| Y1 | Tray icon present | Start the app; look in the notification area (or its overflow ^) | The original dark-tile icon with the tooltip "Dynamic Island" | NOT_RUN |
| Y2 | Left click toggles | Click the tray icon twice | The notch hides, then returns | NOT_RUN |
| Y3 | Keyboard | Win+B to reach the notification area, arrow keys to the icon, Enter; then Shift+F10 | Enter toggles the island; Shift+F10 opens the menu, arrow keys and Enter work, Esc closes | NOT_RUN |
| Y4 | Right-click menu | Right-click the icon; click elsewhere | The menu shows "Hide island"/"Show island" and "Exit"; it closes when you click elsewhere | NOT_RUN |
| Y5 | Exit from tray | Menu → Exit | The app exits and the icon disappears immediately (no ghost icon on hover) | NOT_RUN |
| Y6 | Explorer restart | Task Manager → Windows Explorer → Restart | The icon comes back within a few seconds and still works | NOT_RUN (automated simulation PASS) |
| Y7 | High DPI | Set 150 % or 200 % scaling, restart the app | The tray icon is crisp, not blurry | NOT_RUN |
| TA1 | Pin (P03-02) | Menu → Pin; move the mouse away; menu → Pin again | While pinned the island stays expanded and the item is checked; unpinning lets it collapse. Pin is disabled while the island is hidden | NOT_RUN |
| TA2 | Pause/Resume | Menu → Pause; wait 10 s; menu → Resume | Pause hides the island and the item reads "Resume"; Resume brings it back. Left-clicking the icon while paused also resumes | NOT_RUN (automated PASS) |
| TA3 | Display select (2 monitors) | Connect a second monitor; menu → Display → pick the other display | Items read "Display N (primary)" ordered left to right with the current one checked; the island moves to the chosen display and stays there after a restart | NOT_RUN |
| TA4 | Reset to primary | Drag the island far off (or select the secondary display), then menu → Display → Reset to primary display | The island returns to the default spot on the primary display; offsets are reset | NOT_RUN (automated PASS, settings saved) |
| TA5 | Updates disabled | Open the menu; hover "Check for updates" | It is greyed out with a tooltip saying update checks arrive later; nothing happens on Enter. "Settings…" is enabled (SW1) | NOT_RUN |
| TA6 | Open logs folder | Menu → Open logs folder | File Explorer opens the logs folder (`%LOCALAPPDATA%\DynamicIsland\logs`, or the portable data folder) | NOT_RUN |
| TA7 | Restart (started directly) | Start `DynamicIsland.App.exe` directly; menu → Restart | The island disappears and returns within a few seconds; only one instance runs afterwards | NOT_RUN (automated PASS) |
| TA8 | Restart (via launcher) | Start `DynamicIsland.Launcher.exe`; menu → Restart | Same as TA7 | NOT_RUN (automated PASS) |
| TA9 | Safe Mode enter/leave | Menu → Restart in Safe Mode; check the tooltip; then menu → Leave Safe Mode | The app comes back with the tooltip "Dynamic Island (Safe Mode)" and the item reads "Leave Safe Mode (restart)"; leaving restarts normally | NOT_RUN (automated PASS for enter) |
| TA10 | Exit and keyboard | Shift+F10 on the icon, arrow keys through every item including the Display submenu, Esc, then menu → Exit | Every item is reachable with the keyboard and has an access key; Exit removes the icon and ends the app | NOT_RUN |
| SW1 | Open settings (P03-03) | Tray menu → Settings…; select Settings… again | One "Dynamic Island settings" window opens with a taskbar button; the second request brings the same window to the front | NOT_RUN (automated PASS) |
| SW2 | Keyboard only | In the settings window use Tab, Shift+Tab, arrow keys on the page list, Ctrl+Tab, Alt+access keys, Ctrl+S, Esc | Every control is reachable with a visible focus; Ctrl+S saves; Esc closes | NOT_RUN |
| SW3 | Save and Revert | Change the opacity → Save; change the theme → Revert | Save applies the opacity to the island at once and it stays after a restart; Revert restores the theme and Save is greyed out | NOT_RUN (automated PASS for the save path) |
| SW4 | Validation | Type `abc` as the horizontal offset, then go to the General page | The message appears under the field, Save is greyed out and the status line names the Appearance page | NOT_RUN (automated PASS) |
| SW5 | Close with unsaved changes | Change a value → close the window with the X; repeat with an invalid value | A Save / Don't save / Cancel question (OK/Cancel for invalid values); Cancel keeps the window open; closing never exits the app | NOT_RUN (closing without changes automated) |
| SW6 | Reset | General → Reset all settings… → OK → Save | All pages show defaults; the display returns to primary; the Start with Windows and update-check states are unchanged | NOT_RUN (automated PASS in unit tests) |
| SW7 | Diagnostic report | Diagnostics → Prepare diagnostic report; read the files; Save report… | The preview lists summary, app log and launcher log with sizes; the zip is written only after Save report, contains exactly those files plus manifest.json, and has no settings values or personal paths | NOT_RUN |
| SW8 | DPI and high contrast | Open settings on a 150 % or 200 % monitor; then turn on a Windows contrast theme | Text is sharp and nothing is clipped; the window follows the contrast colours and error messages stay readable | NOT_RUN |
| ST1 | First run (P03-04) | Delete `%LOCALAPPDATA%\DynamicIsland\data\settings.json` (or use a new Windows account) and start the app | "Welcome to Dynamic Island" appears over the island with both consent boxes unchecked and all widgets ticked; Tab and Space work; Continue closes it and it does not appear on the next start | NOT_RUN (automated PASS) |
| ST2 | Real sign-in | Tick "Start Dynamic Island when I sign in" and Continue; sign out and in again (or restart Windows) | About 5 s or more after the desktop appears, the collapsed island shows; no welcome window; only one instance | NOT_RUN (5 s delay automated) |
| ST3 | Disabled in Windows | Settings › Apps › Startup → turn Dynamic Island off; open Dynamic Island settings → General; sign in again | The General page says it is turned off in Windows; it does not start at sign-in; Dynamic Island never turns it back on | NOT_RUN |
| ST4 | Turn off in the app | Settings → General → clear "Start when I sign in to Windows" → Save; sign in again | `HKCU\…\CurrentVersion\Run` has no DynamicIsland value; nothing starts | NOT_RUN (automated PASS) |
| ST5 | Moved portable copy | With startup on, move the whole app folder elsewhere, start it from the new folder | The Run value now points to the new folder's DynamicIsland.Launcher.exe; the next sign-in starts it | NOT_RUN (unit tests PASS) |
| ST6 | Policy | On a test PC, set `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisableCurrentUserRun` = 1 (DWORD), turn startup on | Settings says your organization does not allow it; no Run value is written; remove the policy value afterwards | NOT_RUN |
| ST7 | Second copy | With startup on for copy A, start copy B and open its settings | B says another copy starts at sign-in and does not change the Run value until you clear, tick and save the box in B | NOT_RUN (unit tests PASS) |
| CK1 | Notch clock (ADR 0016) | Start the app and look at the notch | The notch shape is unchanged; it shows only the time (e.g. "2:42 PM", your Windows format), sharp, with no camera, sensor or speaker artwork; the minute changes on time | NOT_RUN (visual reference automated) |
| CK2 | Clock options | Settings → Appearance → Clock: try each time format, Show seconds and each date format, watch the preview, then Save | The preview matches each choice; after Save the notch shows the same text at once | NOT_RUN |
| CK3 | Region change | Windows Settings → Time & language → change the time format to 24-hour (or the region), keep the island open | Within a second the notch follows the new format when "Use Windows setting" is chosen | NOT_RUN |
| CK4 | Seconds and CPU | Turn on Show seconds; watch Task Manager for a minute | Seconds tick smoothly; Dynamic Island stays near 0 % CPU | NOT_RUN |
| CK5 | Clock off, scaling, sleep | Turn off "Clock and date" on the Widgets page; then turn it on, set Size to 200 %; then sleep and wake the PC | Off: plain black notch. 200 %: text scales with the notch. After wake: the correct time at once | NOT_RUN |
| CW1 | Clock widget (P04-02) | Click the island to expand it | Below the title: the time in large type and the full date (e.g. "Monday, October 26, 2026"); the notch line above keeps your clock options | NOT_RUN (automated with the real widget) |
| CW2 | Hidden and Safe Mode | Hide the island from the tray for 2 minutes, then show it; then tray → Restart in Safe Mode | After showing, the time is correct at once. Safe Mode: the notch is plain black and the widget area shows the hint | NOT_RUN |
| CW3 | Clock off/on | Widgets page: untick "Clock and date", Save; tick it again, Save | Off: notch black, no clock in the expanded panel. On: both come back within a second | NOT_RUN (automated) |
| MD1 | Media widget (P04-03) | Play music in a browser tab (YouTube) or a player app (Spotify, VLC, Media Player); expand the island | Title, artist and album art appear within a second; the play/pause icon matches; changing tracks updates it | PARTIAL PASS 2026-10-26 (agent, Media Player 11, owner's file "Bat Country": title and album art shown after the art-cap fix; no artist tag in that file) |
| MD2 | Buttons and keyboard | Click previous, pause, play, next; then Tab to each button and press Space | The player reacts to each; buttons the player turns off are dimmed with a "not available right now" tooltip (Media Player: no Previous on the first queued track, no Next on the last); focus ring visible | PARTIAL PASS 2026-10-26 (agent, Media Player queue of 3: Next and Previous changed tracks through the app's adapter; Previous dimmed on the first track; clicks by mouse/keyboard still owed) |
| MD3 | Several players | Play in two apps, then close the current one | "· 1 more playing" shows; closing the player switches to the other or clears the widget without errors | NOT_RUN |
| MD4 | Nothing playing | Close all players | The media row disappears; nothing else changes; the app log shows only "no-media", never a track name | NOT_RUN |
| MD5 | Media off | Widgets page: untick Media, Save | The media row disappears; turning it back on brings it back for the current track | NOT_RUN |
| SM1 | Metrics widget (P04-04) | Expand the island and watch for 10 s; compare with Task Manager › Performance | CPU, memory (used / total GB) and up time roughly match Task Manager; values update about every 2 s; CPU shows "—" for the first half second | NOT_RUN (scripted cadence automated) |
| SM2 | Battery | On a laptop: unplug and plug in the charger; on a desktop: just look | Laptop: battery % with ⚡ while charging. Desktop: no battery tile | NOT_RUN |
| SM3 | No work while collapsed | Collapse the island, leave it 1 minute; watch Dynamic Island in Task Manager | CPU stays near 0 %; metrics are not sampled until you expand again | NOT_RUN (automated with a scripted service) |
| SM4 | Sleep and resume | Expand, sleep the PC, wake it, expand again | No absurd CPU value after waking; up time continues | NOT_RUN |
| VO1 | Volume widget (P04-05) | Expand the island; drag the volume slider; click it at another point; use arrow keys and Page Up/Down | Windows volume follows (the Windows volume flyout agrees); the slider does not jump back while dragging; the % updates | NOT_RUN (scripted service automated; real Core Audio read-only automated) |
| VO2 | Mute and external changes | Click the speaker button; then use the keyboard volume keys or the Windows quick settings | Mute toggles (icon and tooltip "Unmute"); outside changes appear in the island within a moment | NOT_RUN |
| VO3 | Device switching | Switch the default output (e.g. plug in headphones or a USB/Bluetooth headset, or pick another output in quick settings) | The slider shows the new device's volume; no error; switching back works | NOT_RUN |
| VO4 | No output device | Disable all playback devices (Sound settings) or unplug the only one | "No audio output" with disabled controls; re-enabling brings the slider back | NOT_RUN (view automated) |
| VO5 | Content-sized island | Turn widgets off and on (Widgets page) while expanded or before expanding | The expanded island is only as tall as its content (minimum 180, maximum 360 DIP at 100 %); it grows and shrinks smoothly; nothing is cut off | NOT_RUN (measurement automated) |
| NT1 | Tasks (P04-06) | Expand the island, type in "Add a task", Enter; tick it with the mouse, then Tab to it and press Space | The task appears; ticking strikes it through and moves it below open tasks; focus stays on it | NOT_RUN (automated with the real store) |
| NT2 | Edit and delete | Task: F2 or double-click, change text, Enter (and once Esc); note: click it, Shift+Enter for a new line, Enter; delete one of each with × | Edits are saved (Esc keeps the old text); deleted items disappear | NOT_RUN (edit automated) |
| NT3 | Esc in a text box | Type in "Add a note", press Esc; press Esc again | First Esc clears the box; the second collapses the island | NOT_RUN (first Esc automated) |
| NT4 | Persistence | Add notes and tasks, exit from the tray, start again | Everything is still there, in the same order | NOT_RUN (automated in unit tests) |
| NT5 | Many items and scrolling | Add 8 tasks and 5 notes with all widgets on | Tasks show 6 + "+N more", notes 3 + "+N older notes"; the panel scrolls at 360 DIP; nothing is cut off | NOT_RUN |
| NT6 | Privacy | After NT1–NT5, open the logs folder and a diagnostic report preview | No note or task text appears anywhere; `data\islands.db` is not in the report | NOT_RUN |
| BK1 | Export (P04-07) | Settings → Privacy → Export backup…, choose a folder | A `dynamicisland-backup-<date>.zip` appears; the status names the note and task counts; choosing an existing name is refused | NOT_RUN (automated) |
| BK2 | Restore round trip | Add a note, export; delete the note; Restore from backup…, pick the file, read the summary, OK | The summary shows date, version and counts; the app restarts and the note is back; `data\rollback-<date>` holds the previous files | NOT_RUN (automated: service + startup smoke) |
| BK3 | Cancel and refusals | Restore → Cancel; then pick a random ZIP, a renamed text file, and a backup edited in a ZIP tool | Cancel changes nothing; the others are refused with a short reason and nothing changes | NOT_RUN (automated) |
| BK4 | Newer version | Restore a backup made by a newer Dynamic Island (or ask the developer for one) | Refused: "made by a newer version"; nothing changes | NOT_RUN (automated) |
| BK5 | Privacy | Open the backup ZIP | Only `manifest.json`, `settings.json`, `islands.db`; the manifest has counts and checksums but no note or task text and no paths | NOT_RUN (automated) |
| TB1 | Widget tabs (ADR 0025) | Start the app, expand the island; click each icon tab | The first tab in your widget order is shown first (not whichever widget loaded first); one widget at a time; the selected tab is green; the island grows or shrinks to fit that widget; the small top time hides only on the clock tab | NOT_RUN (automated) |
| TB2 | Keyboard | Expand, Tab to the tab row, use Left/Right; then Ctrl+Tab and Ctrl+Shift+Tab from inside a widget | Arrow keys move between tabs; Ctrl+Tab cycles forward and wraps; focus rings visible; Narrator reads each tab's name | NOT_RUN (cycling automated) |
| TB3 | Turning widgets off | Select the Stats tab; Settings → Widgets: untick System stats, Save; then turn off all but one widget | The selection moves to the first tab; with one widget left the tab row disappears | NOT_RUN (automated) |
| TH1 | Theme (P05-01, ADR 0026) | Settings → Appearance → Theme: Light, Save; then Dark, Save; expand the island with each | Light: light grey island, dark text, green pin readable; Dark: the classic black notch. Every tab (clock, media, stats, volume, tasks, notes) follows at once, including text boxes and buttons | NOT_RUN (palette switch and renderings automated) |
| TH2 | Follow Windows | Theme: Use Windows setting, Save; Windows Settings → Personalization → Colors → switch "Choose your mode" between Light and Dark | The island follows within a second, without restarting | NOT_RUN |
| TH3 | Safe size | Set Size to 200 % on a laptop screen (or set Windows display scale to 175–200 %); expand the island | The island is made smaller so nothing is cut off; on a large monitor the 200 % size comes back; the Size slider still shows 200 % | NOT_RUN (rule unit-tested) |
| SC1 | Global shortcut (P03-05) | Settings → Behavior → tick "Open and close the island with a global shortcut", keep Ctrl+Alt+I, Save; in another app press Ctrl+Alt+I twice | The status says On; the first press opens the island with keyboard focus on it (Tab reaches the pin button); the second closes it and returns focus to the app | NOT_RUN (automated with a posted WM_HOTKEY) |
| SC2 | Conflict | Pick a shortcut another running app already registered (or one Windows owns, e.g. Ctrl+Alt+Del is refused as invalid; try Shift+Win+S), Save | The status explains the shortcut is used by another app or Windows; nothing else changes | NOT_RUN (automated with a second registration) |
| SC3 | Validation and off | Type `Ctrl+C`, then `Alt+F`, then clear the box; then untick the option and Save | Each shows a message and Save is greyed out; when turned off, the status says Off and Ctrl+Alt+I does nothing | NOT_RUN (automated in unit tests) |
| SC4 | Hidden, paused, fullscreen | Hide the island from the tray and press the shortcut; Pause and press it; start a fullscreen video and press it | Hidden and paused: the island comes back expanded. Fullscreen: nothing appears over the video | NOT_RUN (action unit-tested) |

## G04 — Core widgets and data (automated evidence)
| # | Guide G04 requirement | Evidence | Status |
|---|---|---|---|
| G4-1 | All core widgets work without internet | No widget or store uses the network (ADRs 0019–0024); all widget tests run offline with local fakes or local Windows APIs | PASS (automated) |
| G4-2 | Survive 100 restarts | `RestartSmokeTests` (Debug): 100 start/exit cycles of the real app on one data folder; each exits 0, notes/tasks and `settings.json` byte-identical, no corrupt or temp files; logs show no Failed widget, no error and no unclean session | PASS (automated) |
| G4-3 | Update-shaped migrations | Settings: `Legacy_v0_fixture_is_backed_up_migrated_and_saved`, failed migration read-only. Data: `Update_shaped_migration_keeps_notes_and_tasks` (v1 → rehearsal v2, then the older build opens it read-only) and `Failed_update_migration_rolls_back_and_keeps_data` | PASS (automated) |
| G4-4 | One failing widget never crashes others | `WidgetRegistryTests` failure isolation; failed widget's tab shows a note (`WidgetPanelTests`) | PASS (automated) |
| G4-5 | Real hardware | Rows MD, SM, VO, NT, BK, TB above | Owed by the owner |

## G01 — Reliability (all automated; recorded for completeness)
| # | Scenario | Status |
|---|---|---|
| R1 | Corrupt settings recover | PASS (automated + manual run 2026-10-25) |
| R2 | Widget failure isolated | PASS (automated) |
| R3 | Exit never auto-relaunches | PASS (automated, real launcher) |
| R4 | Third crash does not loop | PASS (automated end-to-end in Debug) |
| R5 | No duplicate app | PASS (automated) |
