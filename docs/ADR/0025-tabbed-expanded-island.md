# 0025 — Tabbed expanded island

- Status: Accepted
- Date: 2026-10-26
- Task: owner request (2026-10-26) during P04, amending ADR 0018 §6 and ADR 0022 §5

## Context
- The guide asks for a "click-to-expand dashboard" and does not say how widgets are arranged. ADR 0018 stacked every
  enabled widget; with six widgets the panel needed a scroll bar at the 360 DIP maximum.
- The owner asked: "why everything is shown at once? wasnt the plan to show in individual tabs?" and chose **one widget
  per tab** from three options (tabs, overview + tabs, keep stacked).

## Decision
1. **Layout:** one row with the icon tabs on the left and the pin on the right; the "Dynamic Island" title was removed
   at the owner's request on 2026-10-26 (the tabs say what is shown). One tab per enabled widget that has something to
   show (or that failed), in the user's widget order (Settings → Widgets). Only the selected widget's view is shown
   below; the island height follows that widget (ADR 0022, 180–360 DIP). With a single widget the tabs are hidden and
   the pin stays.
2. **Tabs:** icon buttons (Segoe Fluent Icons: clock, music note, diagnostic, volume, checklist, quick note) with the
   widget name as tooltip and UIA name, AutomationIds `Tab.<id>`. Radio semantics: the selected tab is highlighted
   (green, like the pin); Tab reaches the row once, arrow keys move between tabs; **Ctrl+Tab / Ctrl+Shift+Tab** switch
   tabs from anywhere in the expanded island, wrapping around.
3. **Selection:** until the user picks a tab, the first tab in the user's order is shown, even when another widget
   published content first (widgets start in any order; CI run 40335768082 showed an automatic pick sticking). A tab
   the user picked (click, arrow keys, Ctrl+Tab) stays selected across updates while its widget exists; if that widget
   disappears (turned off or no content), the pick is forgotten and the first tab is shown. Session only (not saved).
4. **Work only for the visible tab:** `WidgetRegistry.SetExpanded(visibleWidgetId)` tells only the selected widget that
   its expanded view is on screen, so the system stats widget samples only while its tab is shown (ADR 0021).
   Because a tab exists only for a widget with content, the stats widget publishes a placeholder without readings
   (up time only, from the tick count) when it starts; without it the stats tab never appeared (owner report,
   2026-10-26).
5. **Clock:** the notch's top-row time is hidden only while the clock tab is shown (ADR 0016/0019 "never twice").
6. Views stay persistent (ADR 0022): switching tabs re-parents one element; focus inside a widget survives updates.

## Alternatives considered
- Overview tab plus detail tabs — offered; the owner chose one widget per tab.
- Keep the stacked dashboard — offered; rejected by the owner.
- Saving the last tab in settings — deferred: a disk write per tab switch for little value.

## Consequences
- The scroll viewer stays as a safety net for very tall content (many tasks). It may scroll only when the selected
  tab is taller than the 360 DIP maximum (`NotchView.FitExpandedHeight`); otherwise a scroll bar flashed while the
  island grew to a taller tab (owner report, 2026-10-26).
- Manual rows TB1–TB3 in `docs/TEST_MATRIX.md`.
