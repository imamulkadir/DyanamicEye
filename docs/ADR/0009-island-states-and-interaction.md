# 0009 — Island state machine, interaction and accessibility

- Status: Accepted (sensor housing replaced by the clock: ADR 0016)
- Date: 2026-10-25
- Task: P02-03

## Context
- Guide §4.1 defines seven states and these rules: hover → peek after a debounce, click → expanded, pointer exit → collapsed unless pinned, Escape → collapsed, a pin stays expanded except under fullscreen or a privacy override, fullscreen auto-hide restores the previous visible state, and Pause hides the island while the tray remains.
- D07: click expands, hover previews, collapse is explicit.
- D08/D10: motion is customizable and adaptive.
- §4.1 also requires correct keyboard focus, no forced focus, and no invisible interaction blockers.

## Decision
1. **Pure transition function** `Core/Shell/IslandTransitions.Next(snapshot, input)`. The snapshot holds state, restore state, pointer inside, and fullscreen active. Clarifications to §4.1:
   - Peek and Expanded are transient: after fullscreen, pause or hide they restore as **Collapsed**, because the pointer has moved on. **Pinned restores as Pinned.**
   - Escape collapses from any visible state **and clears the pin**.
   - Unpinning returns to Expanded only while the pointer is inside; otherwise to Collapsed.
   - Whether a fullscreen app is active is tracked even while hidden or paused. Resume or Show therefore go to `SuspendedFullscreen` if a fullscreen app is still in front.
   - Hidden, Paused and SuspendedFullscreen ignore pointer, click and pin input.

   An **exhaustive 7 × 12 state × input table test** pins down every transition.
2. **`IslandStateMachine`** adds the 250 ms hover debounce (`Debouncer` + `TimeProvider`, with `Cancel` on leave or any state change) and posts the elapsed signal back to the UI dispatcher. `StateChanged` fires only on actual changes.
3. **Fixed-size window, animated pill.**
   - The window is always the expanded size (420 × 180 DIP × appearance scale) and placed by the calculator. The visible `IslandSurface` inside animates its width, height and corner radius between the state sizes: collapsed 200 × 36, peek 280 × 44, expanded and pinned 420 × 180.
   - Animating the window rectangle per frame (`SetWindowPos`) would stutter on layered windows. Fully transparent pixels are not hit-testable, so the unused area **never blocks clicks**. The smoke tests check the corner and the area under the collapsed pill.
   - Hidden states hide the window entirely.
   - The pill is aligned left, center or right within the window according to the placement setting.
4. **Motion:** 160 ms cubic ease-out on size, only when motion is enabled. `Motion=Full` animates, `Reduced` and `Off` change sizes instantly, and `System` follows Windows' "Animation effects" setting (`SystemParameters.ClientAreaAnimation`). Nothing animates while idle.
5. **Keyboard and focus:**
   - The window isn't activated when shown. A click activates it normally, because `WS_EX_NOACTIVATE` is deliberately not used.
   - Keys: Enter or Space expands the focused pill, Escape collapses, and Ctrl+P toggles the pin. The pin button is reachable with Tab and has a visible focus ring.
   - When the island collapses while it is the active window, focus goes back to the window recorded from `WM_ACTIVATE`'s lParam. It uses `SetForegroundWindow`, which Windows only honours while we are foreground, so nothing is forced.
6. **Accessibility:** `IslandSurface` exposes a UI Automation peer with **ExpandCollapse** and **Toggle** (pin) patterns, the stable AutomationId `IslandSurface`, and per-state names ("Dynamic Island, expanded and pinned"). Smoke tests drive the real app through UI Automation, without synthetic mouse input on the user's desktop.
7. **Expanded content** is a placeholder ("Widgets will appear here.") with a pin toggle in the system icon font (Segoe Fluent Icons / MDL2). It ships no asset. The widgets arrive in P04.

## Amendment — notch design (2026-10-25, owner request)
The owner asked for the island to look like a **classic display notch (iPhone 12 Pro era)** rather than a floating pill.
- **`IslandSurface`** is a custom-drawn `Decorator`. Its shape is a flat top flush with the screen edge, 6 DIP **concave shoulders** at the top corners, straight sides, and rounded bottom corners (`min(height/2, 22 DIP)`). Fill is pure black.
- **Hit testing** uses the same geometry (`HitTestCore`), so only the notch is clickable. An integration test locks the outline: inside the shoulder curves and outside the bottom corners counts as "outside".
- **Sensor housing** (superseded by ADR 0016, which shows the clock in this row): a row 32 DIP tall, visible in every state. It has a centered speaker-grille slit, the front-camera lens to its right (dark lens with a small blue glint) and a faint sensor dot to its left. It is drawn from primitives; no images or third-party artwork.
- **Expanded content** sits below the sensor row, so the notch "grows down" from the top edge.
- **Sizes** (shoulders included): collapsed 236 × 32, peek 264 × 38, expanded and pinned 420 × 180 DIP.
- **New defaults:**
  - `Appearance.VerticalOffset` **0** (attached to the top of the work area, below a top taskbar if there is one)
  - `Appearance.Opacity` **1.0**

  Settings are not saved anywhere yet, so no stored files change. Users who saved other values keep them.
- The old green status dot is gone. Status indication returns with widgets (P04).
- The app icon still shows the earlier capsule motif. Updating it to a notch is optional and was not requested yet.

## Consequences
- P02-04 feeds `FullscreenStarted`/`FullscreenEnded`. P03's tray feeds `Pause`/`Resume`/`Show`/`Hide`.
- Notice rendering (ADR 0007) and widgets live inside the expanded panel and must keep the pill's keyboard behaviour.
- Peek has no distinct content yet. P04 can show a one-line summary there.
