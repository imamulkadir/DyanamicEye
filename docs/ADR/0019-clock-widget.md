# 0019 — Clock and date widget

- Status: Accepted
- Date: 2026-10-26
- Task: P04-02

## Context
- Guide P04-02: clock/date widget, time and locale formats, "timers only if approved for v1", avoid 60 fps ticking.
- ADR 0016 put a clock in the notch, driven by an App-side timer (`NotchClock`). ADR 0018 introduced isolated
  widgets with enable/disable, failure isolation and Safe Mode handling. The owner was asked whether countdown timers
  or a stopwatch are wanted and did not ask for them.

## Decision
1. **One clock widget** (`Widgets/ClockWidget : IWidget`, in `BuiltInWidgets`) owns the ticking and formatting and
   publishes an immutable `ClockSnapshot` (`LocalTime`, `Compact` notch text, `Time`, long `Date`). The App-side
   `NotchClock` is removed; the notch text is the clock widget's collapsed view, set by `WidgetPresenter`.
2. **Ticking:** `Task.Delay` on the injected `TimeProvider` until the next whole minute (or second when seconds are
   shown) plus 15 ms; settings changes, activation and system changes wake it at once. Never per frame.
3. **Active/Inactive** (guide §4.2): `IWidget.SetActive` / `OnSystemChanged` (default no-ops). The presenter makes
   widgets inactive while the island is hidden, paused or suspended for a fullscreen app; an inactive clock waits
   without ticking and refreshes when it becomes active. The presenter forwards `SystemEvents.TimeChanged` (after
   clearing the time-zone cache), region-format changes (after clearing the culture cache) and resume.
4. **Expanded view:** the time in large light type (34 px) with the full date below (`LongDatePattern` of the
   Windows region), in the widget slot; its UIA name is "time, date".
5. **No countdown timers or stopwatch** in v1 (not approved); add them only after an explicit owner decision.
6. Consequences of being a widget, amending ADR 0016: turning the "Clock and date" widget off, a clock failure
   ("isn't available"), or Safe Mode (optional widgets do not run) leave the notch plain black.

## Alternatives considered
- Keeping the App-side `NotchClock` next to a widget — rejected: two tickers and two sources of truth for one clock.
- A WPF `DispatcherTimer` inside the widget — rejected: widgets are UI-free (Core/Widgets) and testable without WPF.

## Consequences
- Manual rows CW1–CW3 in `docs/TEST_MATRIX.md`; CK1–CK5 (ADR 0016) still apply.
