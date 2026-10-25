# 0008 — Island window, monitor identity and placement

- Status: Accepted
- Date: 2026-10-25
- Task: P02-01

## Context
- Guide P02-01 requires: top-center default, configurable offsets, the work area, display ID persistence and a primary-monitor fallback.
- §4.1 asks for tool-window behavior only where appropriate, no-activate handling that doesn't block interaction, and no click interception outside the pill.
- D05: the user selects the monitor, with a primary fallback. D08: placement is customizable.

## Decision
1. **Pure placement:** `Core/Display/IslandPlacementCalculator` decides the monitor and rectangle; Win32 code only moves the window.
   - **Monitor:** the preferred monitor (case-insensitive id), else the primary, else the first monitor.
   - **Size:** island DIPs × the monitor's own DPI scale × `Appearance.Scale`.
   - **Position:** inside the **work area**, never under a taskbar on any edge.
   - **Offsets** (new settings `HorizontalOffset`, default 0, ±4000 DIP; `VerticalOffset`, default 8, 0–1000 DIP): the horizontal offset moves right for top-left/center and inward for top-right. These are new properties with defaults, so no schema bump is needed (ADR 0003 rule 7).
   - The result is clamped fully on-screen.
   - **Fallback:** when the preferred monitor is missing, the result reports `UsedFallback`, and **the stored preference is not overwritten**. The island returns to that monitor when it reappears (re-placement runs on display changes).
2. **Monitor identity:** the device interface path from `EnumDisplayDevices(..., EDD_GET_DEVICE_INTERFACE_NAME)`, falling back to `\\.\DISPLAYn`. It is stable across reboots for the same monitor and port. It contains hardware identifiers, so it is stored only in local settings and **never logged** or included in diagnostics.
3. **Window:**
   - WPF `Window` with no chrome, `AllowsTransparency`, `Topmost` and `ShowActivated=false`.
   - **`ShowInTaskbar=True` plus `WS_EX_TOOLWINDOW`** (applied in `SourceInitialized`): `ShowInTaskbar=False` would make WPF create a hidden owner window, which breaks top-level window semantics.
   - `WS_EX_NOACTIVATE` is **not** used, because it would block keyboard interaction once the pill expands (§4.1).
   - Transparent corners of the layered window are click-through, so only the visible pill takes input.
4. **DPI:** `app.manifest` declares **PerMonitorV2** (and `asInvoker`). Placement uses physical pixels through `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`. It is recomputed on `DpiChanged`, `WM_DISPLAYCHANGE` and `WM_SETTINGCHANGE(SPI_SETWORKAREA)`, coalesced into a single pass at background priority.
5. **Artwork:** the island is an original plain capsule (`#111214`, 1 px light border, green status dot). No third-party or proprietary assets.

## Consequences
- P02-02 extends this with full DPI and display-change verification (a 100–200 % matrix, unplug and rotate). It also hit-tests the expanded states.
- The P03 tray "select monitor" stores `MonitorInfo.Id` in `General.PreferredMonitorId`, and "restore monitor" clears it.
- Smoke tests read the real window rectangle with `IslandWindowInterop.GetBounds`. Release smoke runs assume default appearance settings in the test machine's profile.
