# 0022 — Volume widget, persistent widget views and a content-sized expanded island

- Status: Accepted
- Date: 2026-10-26
- Task: P04-05

## Context
- Guide P04-05: volume/mute widget; default device changes, missing output device, external changes; don't assume
  per-app volume. Guide §3 contract `IAudioService`. AGENTS.md: keyboard accessible, no polling, offline.
- The expanded island grew with every widget (180 → 212 → 252 px, ADRs 0020/0021). A volume row would need more,
  while a user with one widget enabled would see a mostly empty panel.
- `WidgetPresenter` rebuilt every widget element on each redraw. A slider rebuilt while the user drags it loses the
  drag, so interactive widgets need views that persist and update in place.

## Decision
1. **Core Audio through classic COM interop** (`Platform.Windows/Audio`): `MMDeviceEnumerator` →
   default render endpoint (`eRender`, `eMultimedia`) → `IAudioEndpointVolume` (master scalar volume and mute) with
   `IAudioEndpointVolumeCallback` for changes from Windows, keyboards or other apps, and `IMMNotificationClient` for
   default-device changes and removal. Standard user, no polling, no per-app (session) volume. Our own changes carry an
   event-context GUID so their echo is not treated as an external change. No output device → the widget shows
   "No audio output" with disabled controls; a device that disappears mid-call is "no device", never a crash.
2. **Widget** `Widgets/VolumeWidget`: event-driven `VolumeSnapshot(HasDevice, Percent, Muted)`; commands
   `mute-toggle` and `set:<0–100>`.
3. **Persistent views** (`App/WidgetView`): one view object per widget id, created once by a factory and updated
   in place with each snapshot (`Update`). `NotchView.ShowWidgets` only re-parents elements when the order or set of
   visible widgets changes. Clock, media, metrics and volume views follow this pattern.
4. **Volume view:** a mute toggle button (speaker icon, name "Mute"/"Unmute"), a horizontal slider 0–100
   (arrow keys ±2, Page Up/Down ±10, move-to-point on click) and the percentage. While the user drags or
   uses the keyboard the slider is not overwritten by incoming updates; values are sent at most every 50 ms plus the
   final value.
5. **Content-sized expansion:** the expanded/pinned notch height is the measured height of its content, clamped to
   **180–360 DIP** (× appearance scale); width stays 420. The window keeps the maximum size (`IslandLayout.Max` =
   420×360) so placement does not move while widgets change; everything outside the notch stays transparent and
   click-through (ADR 0008). When widget content changes while expanded, the notch re-measures and animates to the
   new height. `IslandLayout.Expanded` is the minimum size (420×180) used by previews and visual references.

## Alternatives considered
- NAudio or another audio library — rejected: a dependency for five COM calls; licence and update churn.
- Polling `GetMasterVolumeLevelScalar` — rejected: the endpoint callback is free and immediate.
- Keeping a fixed, ever-taller expanded size — rejected: wasted space with few widgets, overflow with many.
- Source-generated COM (`GeneratedComInterface`) — deferred: classic `ComImport` is simpler for this small surface;
  revisit if trimming/AOT is adopted in P06.

## Consequences
- The expanded-size smoke test checks the width and a height inside the 180–360 range instead of one fixed size.
- Manual rows VO1–VO5 in `docs/TEST_MATRIX.md` (Bluetooth/USB device switching, volume keys, no device).
