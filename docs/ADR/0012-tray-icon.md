# 0012 — Notification-area (tray) icon

- Status: Accepted
- Date: 2026-10-25
- Task: P03-01

## Context
- Guide P03-01: a native tray icon where left click toggles, right click opens an accessible menu, the icon recovers after an Explorer restart, and it is removed on Exit.
- Guide §2 allows `NotifyIcon` or Win32 `Shell_NotifyIcon`, and requires Explorer-restart re-registration, DPI-aware icons and disposal.
- §0: forced tray visibility is prohibited. Windows decides whether the icon sits in the visible area or the overflow; we never try to pin it.

## Decision
1. **Win32 `Shell_NotifyIcon` adapter** (`Platform.Windows/Tray/Win32TrayIcon`) instead of WinForms `NotifyIcon`.
   - We own the window handle and icon id, so tests can verify registration (`Shell_NotifyIconGetRect`) and simulate an Explorer restart.
   - It avoids pulling WinForms into the WPF app.
2. **Notification-icon version 4.**
   - `NIN_SELECT` is a left click; `NIN_KEYSELECT` is Enter or Space while the icon has keyboard focus.
   - `WM_CONTEXTMENU` covers right click, Shift+F10 and the Menu key.
   - `NIF_SHOWTIP` gives the standard tooltip "Dynamic Island", or "Dynamic Island (Safe Mode)".
3. **Owner window:** a hidden **top-level** `HwndSource` titled `DynamicIsland.Tray`. It can't be a message-only window, because those don't receive the `TaskbarCreated` broadcast.
   - On `TaskbarCreated` the icon re-adds itself.
   - On `WM_CLOSE` (as sent by `taskkill` without `/F`) the app performs a normal **Exit** instead of letting the owner window be destroyed, which would orphan the icon. This was found during manual testing and is now a smoke regression test.
4. **Icon:** the exe's own icon (group 32512, original artwork from ADR 0008/P02-02), loaded with `LoadImageW` at the small-icon size for the owner window's DPI. Windows picks the closest of our 16–256 px sizes, so it stays crisp. It falls back to the shared system application icon.
5. **Menu:** a WPF `ContextMenu` at the mouse point. The owner window takes the foreground first, as `Shell_NotifyIcon` requires, so the menu closes when the user clicks elsewhere. It is keyboard navigable, with access keys and the first item focused. P03-01 has "Show/Hide island" and "Exit"; P03-02 adds the full action list.
6. **Toggle semantics:** left click or Enter on the icon does this:
   - **Hidden** → Show
   - **Paused** → Resume
   - any other state → Hide (`IIslandStateMachine`, ADR 0009)
7. **Without a shell tray** (Server Core, some CI desktops) the app logs a warning and keeps working without an icon. Tests skip with a reason in that case.

## Known limitations
- If a tool sends `WM_CLOSE` to the app while the tray **menu is open**, the close may reach the menu popup, which only dismisses the menu. The app keeps running normally. This is rare and accepted.
- Hiding the island while no tray icon exists leaves only the second-launch activation (ADR 0005) to bring it back.

## Consequences
- P03-02 adds pin, pause/resume, display selection and reset, settings, update check, logs, Safe Mode restart and Exit to the menu. It also decides `ShutdownMode` so that closing a settings window never exits the app.
- G03 requires a real Explorer restart on hardware: Task Manager → Windows Explorer → Restart.
