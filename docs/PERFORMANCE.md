# Performance benchmark (P05-04, G05)

G05 goals (guide §5, **goals, not guarantees**): idle CPU ≤ 0.5 % and private memory ≤ 100 MB on the reference
machine; no runaway timers or obvious UI leaks. Decisions: ADR 0029.

## Setup
- **Test:** `tests/DynamicIsland.WindowsSmokeTests/ResourceBenchmarkSmokeTests.cs` (Debug build: isolated data folder
  through the test hook; runs in CI as part of the Debug smoke suite).
- **App state:** fresh settings with both consents answered (no first-run window), **all six widgets on**, no media
  playing, 1 monitor at 100 %.
- **Driving:** UI Automation (expand, select tabs, collapse) and the tray menu (pause, exit) — no mouse or keyboard
  input on the desktop.
- **Per state:** 4 s to settle, then CPU time over a 15 s window (`Process.TotalProcessorTime`), private bytes
  (`PrivateMemorySize64`) and working set at the end.
- **Output:** `benchmark-results.json` next to the test binaries (CI uploads test results).
- **CPU figures:** "one core" = CPU time ÷ wall time; "whole PC" = that ÷ logical processors (what Task Manager shows).
- **Reference machine:** Windows 11 Pro 25H2 (build 26200) x64, 6 logical processors, .NET 10.0.12.

## Results — 2026-10-27, reference machine (final P05-04 code)

| State | CPU, one core | CPU, whole PC | Private memory | Working set |
|---|---|---|---|---|
| Collapsed, idle | 0.00 % | 0.00 % | 62 MB | 131 MB |
| Open, clock tab | 0.52 % | 0.09 % | 70 MB | 145 MB |
| Open, system stats tab (first 19 s) | 1.56 % | 0.26 % | 75 MB | 151 MB |
| Open, clock tab, after 40 open/close cycles with tab switches | 0.00 % | 0.00 % | 94 MB | 161 MB |
| Paused (tray) | 0.00 % | 0.00 % | 104 MB | 173 MB |

Earlier runs the same day ranged: collapsed 0–0.8 % of one core (≤ 0.14 % of the PC), stats tab 1.6–5.9 % of one
core before the cadence fix.

## Against the G05 goals
- **Idle CPU ≤ 0.5 %: met.** Collapsed and paused are at or near 0 % of the PC; the clock wakes once a minute.
- **Private memory ≤ 100 MB: met at rest (62–77 MB); deviation after heavy use.** After 40 automated open/close
  cycles memory is 94–104 MB. It is not a per-cycle leak: 120 cycles grew it by 25 MB, 40 cycles by 18 MB (not 3×).
  It is mostly the .NET heap's high-water mark plus UI Automation objects the app keeps for the test's automation
  client. Revisit with GC settings (`GCConserveMemory`) only if users report memory above the goal.
- **No runaway timers:** none found; every widget is event-driven or stops when not visible (ADR 0029 context). The
  benchmark fails on runaway values (2 % of one core idle, 15 % with stats, 250 MB, 40 MB growth over the cycles).

## Findings and fixes in P05-04
- A visible stats update cost ~40–80 ms of CPU, almost all on one non-UI thread (the UI thread spent 1–5 ms per
  render; turning off window transparency or forcing software rendering changed little). Each change redraws the
  island window. Fix: the stats tab updates every 2 s for its first 30 s on screen, then every 5 s (ADR 0029).
- The stats widget woke for repeated, unchanged visibility calls; now it wakes only on a real change.

## Battery scenarios
- Manual rows PF1–PF3 in `docs/TEST_MATRIX.md` (laptop on battery, Energy Saver, sleep/resume). Energy Saver already
  turns off island animations (MotionGate).
