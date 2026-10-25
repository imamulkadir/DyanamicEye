# 0011 — Activity budget, power-aware motion and visual regression

- Status: Accepted
- Date: 2026-10-25
- Task: P02-05

## Context
Guide P02-05 asks for an adaptive frame and activity budget, reduced motion, power-aware control, and basic visual QA against a screenshot reference using original artwork only. D10 asks for balanced, adaptive motion that is event-driven where possible.

## Decision
1. **Activity budget: nothing runs while idle.**
   - No timers, polling or running animations exist while the island is collapsed, hidden or paused.
   - Every source of work is event-driven: display messages, `SystemEvents`, WinEvent hooks, input, and one-shot debouncers.
   - Size animations last 160 ms and stop.
   - A smoke test measures the real app's CPU time over 10 s of idle and fails above 300 ms (generous for CI). The dev host measured **0 ms with a 94 MiB working set**.
2. **Motion policy (`Core/Shell/MotionPolicy`, pure + `IMotionGate`).**
   - `Off` or `Reduced` never animate.
   - Battery saver / energy saver never animates (power-aware, D10).
   - `Full` animates otherwise.
   - `System` follows Windows "Animation effects".
   - Inputs come from `MotionGate` (App): the user setting, `SystemParameters.ClientAreaAnimation`, and `GetSystemPowerStatus.SystemStatusFlag`. The power status is cached and refreshed on `SystemEvents.PowerModeChanged`, with no polling.
3. **Visual regression.**
   - The notch visuals live in `NotchView` (a UserControl), so a test renders them **offscreen** with `RenderTargetBitmap` at 96 DPI on a fixed grey backdrop. That is independent of the desktop behind the window and of display scaling, unlike desktop screenshots.
   - Each state (collapsed, peek, expanded, pinned) is compared with a committed reference PNG in `tests/DynamicIsland.IntegrationTests/References/`.
   - Tolerance: a channel delta above 24 counts as a difference, and at most 1 % of pixels may differ. On failure the actual image is saved to `VisualDiffs/`.
   - References are **our own renderings only**. To accept an intentional design change, run the tests with `DYNAMICISLAND_UPDATE_VISUAL_REFERENCES=1` and review the PNG diff in the PR.
4. **Test isolation.**
   - Desktop UI tests share one screen, so CI runs test projects one at a time (`--max-parallel-test-modules 1`).
   - Helper windows are never topmost (ADR 0010).

## Consequences
- Any future periodic work (widgets in P04) must be bounded, honor Safe Mode, and stop when disabled or paused. The idle CPU test guards the collapsed state.
- Font rendering can differ slightly between Windows builds. If the CI runner's rendering differs beyond tolerance, references are regenerated **on CI's image** and reviewed. A per-state tolerance can be raised in a reviewed change, never silently.
