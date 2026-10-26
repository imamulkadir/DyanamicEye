# 0018 — Widget registry and lifecycle

- Status: Accepted
- Date: 2026-10-26
- Task: P04-01

## Context
- Guide P04-01 and D09: compiled-in widgets that can be enabled, disabled and reordered; failures are isolated; memory
  and background work stop when a widget is disabled. Guide §4.2 lifecycle: Created → Initialized → Active →
  Inactive → Disabled/Disposed; providers start asynchronously after the shell appears; no UI-thread blocking;
  cancellation throughout. No external plugins in v1.
- ADR 0007 already provides isolated components (`IIsolatedComponent`, `ComponentBoundary`, `ComponentHost`): error
  boundaries, retries with backoff and jitter, Safe Mode skip, bounded shutdown, pause (ADR 0013). Settings already
  hold the ordered `WidgetSetting(Id, Enabled)` list (ADR 0003), edited on the Widgets page and in onboarding.

## Decision
1. **A widget is an isolated component** (`Core/Widgets/IWidget : IIsolatedComponent`): `Id` (a `WidgetIds` value),
   component `Name` `widget-<id>`, its background work in `RunAsync`, the latest UI content in `Snapshot`
   (immutable, `null` until there is something to show) with `SnapshotChanged`, and `Release()` to drop cached content
   and resources after it stops.
2. **Lifecycle mapping:** Created = constructed by the catalog; Initialized/Active = `RunAsync` running under its
   boundary (Starting → Healthy/Degraded); Inactive = stopped while the island is paused (existing pause); Disabled =
   stopped by the user's setting (status `Stopped`, reason `disabled`) and `Release()`d; Disposed = with the host.
   Failures follow ADR 0007 (`Failed` after the recoverable limit) and never affect other widgets.
3. **`ComponentHost` gains per-component enable/disable** (`Add(..., enabled)`, `SetEnabledAsync`). A disabled
   component is never started, is stopped (bounded wait) when disabled while running, and is not resumed by
   Resume. Enable/disable and pause/resume are serialized. Every non-skipped component is registered for shutdown once.
4. **`WidgetRegistry` (Core)** owns the compiled-in widgets: registers them with the host using the saved
   enabled flags, applies later settings changes (enable, disable + release, order), and exposes the ordered
   **slots** of enabled widgets (id, label, status, snapshot) with a `Changed` event. Unknown or not-yet-built widget
   ids in settings are simply not shown.
5. **Compiled-in catalog** (`Widgets/BuiltInWidgets.Create`) is the only place widgets come from — no reflection or
   plugin loading. It is empty in P04-01; each widget task (P04-02…P04-06) adds its module there.
6. **UI** (`App/WidgetPresenter` → `NotchView` expanded panel): slots in settings order; a failed widget shows
   "<name> isn't available right now" in its slot; widgets without content take no space. With no slots the panel
   keeps its "Widgets will appear here." hint. Snapshot events from background threads are marshalled to the UI
   thread.
7. The notch clock (ADR 0016) keeps using the clock widget's enabled flag; P04-02 moves it behind the clock module.

## Alternatives considered
- A separate widget host beside `ComponentHost` — rejected: it would duplicate error boundaries, Safe Mode and
  shutdown handling.
- Keeping disabled widgets running but hidden — rejected by D09 (disabled widgets must cease background work).
- Discovering widgets by reflection — rejected: no plugin-style loading in v1; an explicit catalog is clearer.

## Consequences
- Each P04 widget implements `IWidget`, adds itself to `BuiltInWidgets`, and adds a view to the presenter.
- Unit tests cover enable/disable/reorder, pause interplay, Safe Mode and failure isolation with fake widgets.
