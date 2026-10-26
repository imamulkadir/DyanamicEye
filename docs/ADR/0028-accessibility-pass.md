# 0028 — Accessibility pass: focus, names, high contrast, target sizes

- Status: Accepted
- Date: 2026-10-26
- Task: P05-03

## Context
- Guide P05-03: keyboard-only flow, focus indicators, AutomationProperties, screen reader check, high contrast, reduced
  motion, minimum touch targets. AGENTS.md: user-facing behavior keyboard accessible, DPI aware, nonintrusive.
- Already in place: the island opens and closes by keyboard (Enter, Escape, Ctrl+P, the global shortcut, ADR 0017);
  UI Automation ExpandCollapse and Toggle on the island; tabs with radio semantics and Ctrl+Tab (ADR 0025); names on
  tabs, pin and widget controls; reduced motion follows Windows "Animation effects", Energy Saver and the Motion
  setting (MotionGate); the Settings window uses system colors in high contrast.
- Audit gaps: the island itself showed no focus mark (its focus visual was off); the volume slider and task check
  boxes used WPF's dotted **black** focus rectangle, invisible on the black island; high contrast was ignored by the
  island (ADR 0026 §6); targets were not checked (note rows were 23 DIP tall, tabs and pin 30); selected tabs and the
  pin differed by color only.

## Decision
1. **Focus marks:** the island surface draws a 2 DIP outline in the theme's focus color inside its shape while it has
   keyboard focus. Slider and task check boxes sit in a border that shows the focus color while focused
   (`ThemeResources.WithFocusRing`); buttons and toggles already had focus borders.
2. **Selection not by color alone:** checked tabs and the pin also get an outline in the accent color.
3. **High contrast:** when Windows high contrast is on, the island uses only the user's system colors
   (`IslandTheme.HighContrast`: Window, WindowText, Highlight; no alpha, no tinted fills), whatever the Theme setting.
   Switching high contrast on/off or between contrast themes re-applies at once (`WM_SYSCOLORCHANGE`,
   `SPI_SETHIGHCONTRAST`, "ImmersiveColorSet").
4. **Target sizes:** every clickable control is at least 24 × 24 DIP (WCAG 2.2 SC 2.5.8, AA); the main controls (tabs,
   pin, media and volume buttons) at least 32 × 32. Sizes scale with the island and DPI.
5. **Automated audit:** an integration test renders every real widget view and checks that each interactive control
   has a screen-reader name and meets the target size; it must check at least 20 controls so it cannot pass empty.

## Alternatives considered
- 40 × 40 DIP (touch guidance) for all controls — rejected for now: the island would need ~25 % more height; 24/32
  meets WCAG AA and the device is mouse/keyboard first. Revisit with touch feedback.
- WPF `FocusVisualStyle` with themed brushes — rejected: focus visuals render in the adorner layer, which does not see
  the island's theme resources.

## Consequences
- Screen reader (Narrator) and real high-contrast checks stay manual: rows AX1–AX4 in `docs/TEST_MATRIX.md`.
- New visual reference `notch-pinned-highcontrast.png`; the others changed slightly (32 DIP pin and tabs, outline on
  selected toggles).
