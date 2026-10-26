# 0026 — Theme tokens, light/dark island and safe size

- Status: Accepted
- Date: 2026-10-26
- Task: P05-01

## Context
- Guide P05-01 / D08: theme tokens and colors, radius/size/opacity/placement, visual hierarchy; enforce min/max safe
  dimensions and visibility.
- Since P03-03 the Settings › Appearance page saved a Theme choice (Use Windows setting / Light / Dark), but nothing
  applied it: the island was always black, with colors hard-coded in XAML and in each widget view.
- Size (0.75–2.0), opacity (0.3–1.0), placement and offsets were already validated (ADR 0003) and applied. The window
  rectangle is clamped to the monitor's work area, but the notch inside it was scaled by the raw setting, so at 200 % on
  a small or high-DPI laptop screen the expanded island (420 × 360 DIP × size) could be cut off.

## Decision
1. **Color tokens by role** (`Core/Shell/IslandTheme`): Surface, TextPrimary, TextSecondary, Accent, AccentFill,
   HoverFill, FieldFill, FocusRing, Placeholder; two palettes:
   - **Dark** (default): exactly the previous colors, so the classic black notch is unchanged (visual references are
     byte-identical).
   - **Light**: light grey surface, near-black text, a darker green accent (the dark-theme green is unreadable on light).
   Shape tokens: shoulder 6, bottom corners 16, control corners 8 DIP (scaled with the island). Radius is not a user
   setting: a different radius would break the notch look, and nobody asked for it.
2. **Visual hierarchy:** primary text for values and titles, secondary (70 % alpha) for labels and hints, accent only for
   the selected/pinned state. Unit tests require WCAG AA contrast against the surface in both palettes: text ≥ 4.5:1,
   accent and focus ring ≥ 3:1.
3. **Applying a theme:** the token brushes live in `NotchView.Resources` (`App/ThemeResources`). XAML uses
   `DynamicResource`; code-built widget views use `SetResourceReference` (`Themed(...)`), and the shared icon-button style
   uses `DynamicResourceExtension` setters. `NotchView.ApplyTheme` swaps the brushes, so every view restyles in place —
   no views are rebuilt and focus/drags survive.
4. **"Use Windows setting"** follows the Windows app mode (`HKCU\…\Themes\Personalize\AppsUseLightTheme`; missing or
   unreadable = dark). Changes arrive as `WM_SETTINGCHANGE` "ImmersiveColorSet" on the island window: no polling. Saving
   a different Theme in Settings applies at once.
5. **Safe size** (`IslandLayout.SafeScale`): the size in use is the user's size, reduced so that the largest island
   fits within 90 % of the monitor's work-area width and 60 % of its height (in DIPs at that monitor's DPI), but never
   below 0.5 (or the user's size if smaller). The saved setting is never changed; moving to a bigger monitor restores it.
   Settings › Appearance says so under Size. Opacity keeps its 30 % minimum so the island never becomes invisible.
6. High contrast mode maps to system colors in P05-03 (accessibility); until then the chosen palette is used.

## Alternatives considered
- WPF `ThemeMode`/Fluent theme (.NET 9+) — rejected for the island: it styles standard controls, while the island is a
  custom-drawn surface; it would also change the Settings window look, which is out of scope here.
- Rebuilding widget views on a theme change — rejected: loses focus, drags and caret positions (ADR 0022).
- Silently lowering the saved Size setting — rejected: the user's choice must survive monitor changes.

## Consequences
- New visual references `notch-collapsed-light.png` and `notch-pinned-light.png`.
- New views must take colors from `ThemeResources` keys, never literals.
- Manual rows TH1–TH3 in `docs/TEST_MATRIX.md`.
