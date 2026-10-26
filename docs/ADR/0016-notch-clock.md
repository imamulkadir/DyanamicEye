# 0016 — Notch clock

- Status: Accepted (§4 and §6 amended by ADR 0019: the clock widget ticks; no clock in Safe Mode)
- Date: 2026-10-26
- Task: owner request (2026-10-26), the collapsed-notch part of P04-02 (clock/date widget), taken ahead of P03-05

## Context
- The notch (ADR 0009) showed a decorative phone sensor housing: a speaker slit, a proximity dot and a front-camera
  lens. A Windows PC has none of these behind the island, so the owner asked to replace them with the time, e.g.
  "2:42 PM", with options for seconds and for dates in different formats. The notch design itself stays the same.
- Guide P04-02: clock/date widget with time and locale formats; avoid 60 fps ticking. ADR 0011: no periodic work while
  idle beyond what is needed; the idle smoke test allows 300 ms CPU in 10 s.
- New settings are needed. ADR 0003 §7: a new property with a default needs no schema bump; older builds ignore it.

## Decision
1. **What the notch shows.** The sensor housing is removed. The top row of the notch (the collapsed height, scaled
   with the appearance size) shows one centred line of text: `[date · ]time`, white, Segoe UI Variable/Segoe UI
   13 px semibold at 100 %, tabular digits, trimmed with an ellipsis if ever too long. It stays visible when the island
   is previewed or expanded. Shape, size and colours are unchanged.
2. **Settings** (`AppearanceSettings.Clock`, schema stays 1):
   - `HourFormat`: `System` (default, the Windows short/long time format), `TwelveHour`, `TwentyFourHour`;
   - `ShowSeconds` (default off);
   - `DateFormat`: `None` (default), `Weekday` ("Mon"), `Date` ("Oct 26"), `WeekdayAndDate` ("Mon Oct 26"), `Numeric`
     (the Windows short date, "10/26/2026"). Names and day/month order come from the Windows region format.
   - The 12-hour format falls back to "AM/PM" for regions without AM/PM names.
   - Older builds ignore the `clock` object. A later new enum value must bump the schema (ADR 0003 §7).
   - Edited on Settings → Appearance → Clock with a live preview; each choice is labelled with an example in the
     user's own format. "Reset this page" on Appearance resets the clock too.
3. **Turning it off** uses the existing "Clock and date" widget switch (Widgets page); the notch is then plain black.
4. **Update timing** (`App/NotchClock`): a one-shot `DispatcherTimer` re-armed for the next whole minute, or second
   when seconds are shown, plus 15 ms — no polling and no animation. It stops while the island is hidden, paused or
   suspended for a fullscreen app, and refreshes at once when the island returns, settings change, the system time or
   time zone changes (`SystemEvents.TimeChanged`), the region format changes (`UserPreferenceChanged`, Locale) or the
   PC resumes.
5. **Pure formatting** in `Core/Shell/ClockFormatter` (culture and time passed in), unit-tested across formats and
   regions. The visual regression references render a fixed "2:42 PM" so they do not depend on the time of the run.
6. The clock is plain presentation, not an isolated optional component, so it also shows in Safe Mode.

## Alternatives considered
- Keeping the sensor artwork and adding the time elsewhere — rejected by the owner; it imitates hardware a PC lacks.
- A 1 s timer always — rejected: 60× more wake-ups than needed when seconds are off (ADR 0011).
- `CompositionTarget.Rendering` — rejected: per-frame work.
- A schema bump to 2 — not needed for an additive property with defaults (ADR 0003 §7).

## Consequences
- P04-02 still owes the expanded clock widget (and any timers if approved); it builds on `ClockFormatter` and these
  settings.
- With seconds on, the layered island window repaints once a second; the idle budget test runs with the default
  (minutes), and manual row CK4 checks the seconds case.
- Manual rows CK1–CK5 in `docs/TEST_MATRIX.md`.
