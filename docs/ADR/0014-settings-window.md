# 0014 — Settings window: navigation, save semantics, validation and reset

- Status: Accepted (§6 amended by ADR 0015: consents are editable)
- Date: 2026-10-26
- Task: P03-03

## Context
- Guide P03-03: a dedicated WPF settings window with General, Appearance, Behavior, Widgets, Updates, Privacy,
  Diagnostics and About, with explicit save or auto-save semantics, validation and reset.
- Settings are owned by `ISettingsService` (ADR 0013). The tray can change the display or offsets while the window is open.
- `SettingsValidator` silently repairs values on load (ADR 0003). A user typing a value needs a visible error instead.
- Consent settings (sign-in startup, daily update checks) must never change without an explicit user choice (D02, guide
  §0). Their real effects arrive in P03-04 (HKCU Run entry) and P06 (update checker).
- ADR 0007 requires that the Diagnostics page shows the bundle preview and writes the zip only after the user confirms.

## Decision
1. **Explicit Save, no auto-save.** The window edits a draft. **Save** (default button, Enter or Ctrl+S) applies it;
   **Revert** discards it; Esc or the close button closes the window. If the draft has unsaved changes, closing asks
   Save / Don't save / Cancel.
   - Why not auto-save: half-typed numbers would be applied to the live island, and a slider drag would write the
     settings file many times.
2. **Pure editor model.** `Core/Shell/SettingsEditor.cs` holds the draft, its validation errors, `IsDirty`, `CanSave`,
   reset and widget reordering. It is unit-tested without WPF. `App/SettingsWindow` only binds to it.
3. **Save merges only what the user changed.** `ApplyTo(latest)` takes each field from the draft only if it differs from
   the value the window loaded; every other field keeps the service's latest value. A display change made from the tray
   while the window is open therefore survives a Save. If the draft is unchanged when an external change arrives, the
   window reloads it.
4. **Strict validation in the editor.** Offsets, hover delay and fullscreen exceptions are typed text. A value outside
   the `SettingsValidator` limits shows a message next to the field (also exposed as the field's UIA help text), and
   Save stays disabled until it is fixed. Sliders and lists can only produce valid values. Numbers use the user's
   culture.
5. **Reset.** Each page has "Reset this page"; General has "Reset all settings". Both only change the draft; nothing is
   written until Save. **Reset never touches consent settings.**
6. **Consents are shown, not edited here** in this task: the Start with Windows and Daily update check states are shown
   as read-only text. P03-04 (onboarding and the Run entry) and P06 (update checker) make them editable together with
   their real effect, so a checkbox never claims a behavior that does not exist yet.
7. **Read-only settings store** (ADR 0003): a banner explains that changes apply until the app restarts. Save still
   applies them for the session.
8. **One window.** The tray's Settings… item opens it or activates the open one. It is a normal resizable window with
   a taskbar button. `TabControl` with left tabs gives keyboard navigation (Tab, arrow keys, Ctrl+Tab). Closing it never
   exits the app (`ShutdownMode=OnExplicitShutdown`, ADR 0013).
9. **Diagnostics page:** "Open logs folder"; "Prepare diagnostic report…" builds the `DiagnosticBundleBuilder` preview
   and shows every file and its size in a read-only viewer; only "Save report…" (a standard save dialog) writes the zip.
   Nothing is uploaded.
10. **Privacy page** is informational: data stays on this PC, its folder location (with "Open data folder"), no
    telemetry or activity tracking, logs are redacted and kept locally.
11. **Save failures** (I/O, permissions) show a message in the window, keep the draft, and are logged by exception type
    only (`SettingsNotSaved`).

## Alternatives considered
- Auto-save with live preview — rejected for now (see 1); live preview can be added later without changing the model.
- Overwriting the whole settings object on Save — rejected: it would undo tray changes made while the window was open.
- Clamping typed values silently like the loader — rejected: the user would not know what was saved.
- A navigation framework or MVVM library — rejected: one window, no new dependency.

## Consequences
- P03-04 adds the consent controls (onboarding plus General/Updates) and the Run entry.
- P05 adds theme and accessibility polish (high contrast, focus visuals); this window uses the standard WPF/system
  styles, so it already follows high-contrast themes.
- Manual rows SW1–SW8 in `docs/TEST_MATRIX.md`.
