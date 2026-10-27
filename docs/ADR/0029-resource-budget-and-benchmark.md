# 0029 — Resource budget, adaptive stats cadence and the benchmark

- Status: Accepted
- Date: 2026-10-27
- Task: P05-04

## Context
- Guide P05-04 / G05: idle CPU and memory and battery scenarios; caching, adaptive timers, suspension when
  fullscreen/paused/disabled; log the benchmark setup and results. G05 goals (not guarantees): idle CPU ≤ 0.5 % and
  private memory ≤ 100 MB on the reference machine; no runaway timers or obvious UI leaks.
- Existing design: no polling anywhere (event hooks for fullscreen, Core Audio and media callbacks, settings and
  display events); the clock wakes once per minute (per second only with seconds shown) and stops while the island is
  hidden; stats sample only while their tab is on screen (ADR 0025); disabled widgets stop and release (ADR 0018);
  paused and fullscreen-suspended islands stop optional work (ADR 0013).
- Measured (docs/PERFORMANCE.md): every visible stats update cost ~40–80 ms of CPU, almost all on one non-UI
  thread; the UI thread spent 1–5 ms. Software rendering and a non-layered window changed little, so the cost is
  redrawing the island window (always 420 × 360 DIP, ADR 0022) for each change, not the readings.

## Decision
1. **Adaptive stats cadence:** every 2 s for the first 30 s after the stats tab appears, then every 5 s while it stays
   on screen (a pinned stats tab); showing it again restarts the quick cadence. CPU after 0.5 s on first show is kept.
2. **No wake without a change:** the stats widget wakes its loop only when its active/visible state really changes
   (repeated calls used to add samples, and each sample costs a redraw).
3. **Benchmark** (`ResourceBenchmarkSmokeTests`, Debug, runs in CI): the real app with every widget on, measured
   collapsed, open on the clock tab, open on the stats tab, open after 40 open/close cycles with tab switches, and
   paused. It writes `benchmark-results.json` and fails only on runaway values (2 % of one core idle, 15 % with the
   stats tab, 250 MB private, 40 MB growth over the cycles), so slow CI runners do not flake; the G05 goals are judged
   on the reference machine and recorded in docs/PERFORMANCE.md.
4. **Not now:** a smaller or per-state window (would undo the fixed placement of ADR 0022), GC tuning
   (`GCConserveMemory`) — revisit only if real users report memory above the goal.

## Consequences
- A pinned stats tab costs about 60 % less CPU after its first 30 s.
- G05 deviations are documented in docs/PERFORMANCE.md (private memory after heavy automated use).
