# 0021 — System metrics widget

- Status: Accepted
- Date: 2026-10-26
- Task: P04-04

## Context
- Guide P04-04: CPU, RAM, battery and uptime; efficient API; handle no battery, permissions, sleep and counter
  resets; adaptive update cadence. Guide §3 contract `ISystemMetricsService`. ADR 0011 activity budget.
- The values are only shown in the expanded island (ADR 0018 panel).

## Decision
1. **APIs** (`Platform.Windows/Metrics/Win32SystemMetrics`), all standard-user and cheap (no performance counters, WMI
   or PDH, which are slow to start and can be disabled by policy):
   - CPU: `GetSystemTimes` (idle, kernel, user); usage = busy / total between two samples (kernel time includes
     idle). A first sample, a counter going backwards or a zero interval give "no value yet", never a wrong number.
   - Memory: `GlobalMemoryStatusEx` (total and available physical memory).
   - Battery: `GetSystemPowerStatus`; `BatteryFlag` 128 means no system battery (the tile is hidden); 255 percent
     means unknown; charging from `BatteryFlag` 8 or AC power.
   - Uptime: `Environment.TickCount64` (time since Windows started; with Fast Startup that is the last full boot or
     hibernate resume, the same as Task Manager).
2. **Pure math** in `Core/Metrics` (`CpuUsage`, `MetricsFormat`), unit-tested.
3. **Adaptive cadence** (`Widgets/MetricsWidget`): samples **every 2 s only while the island is expanded**; collapsed,
   hidden, paused or fullscreen-suspended → no sampling at all. On expanding it takes a baseline and shows CPU after
   0.5 s instead of an average over a long idle gap. Resume from sleep (`OnSystemChanged`) resets the CPU baseline.
   This adds `IWidget.SetExpanded(bool)` (default no-op), forwarded by `WidgetRegistry`/`WidgetPresenter`.
4. **Errors:** an API failure leaves that value out ("—") for that sample; it does not fail the widget.
5. **View:** one row of compact tiles — CPU %, memory used/total GB, battery % with a charging mark (hidden without a
   battery), uptime ("2 d 3 h", "4 h 12 m", "35 m"). Each tile has a UIA name with the full value.
6. **Space:** `IslandLayout.Expanded` grows to 420×**252** so clock, media and metrics fit; the expanded/pinned visual
   references are regenerated. Content-sized expansion is left to P05.

## Alternatives considered
- `PerformanceCounter` / PDH — rejected: heavy first-call cost, needs counter names, can be broken or disabled.
- WMI for battery — rejected: slow and not needed; `GetSystemPowerStatus` covers the tile.
- Sampling while collapsed to keep values warm — rejected: values are not visible then (ADR 0011).

## Consequences
- Manual rows SM1–SM4 in `docs/TEST_MATRIX.md` (laptop with battery, desktop without, sleep/resume, load).
