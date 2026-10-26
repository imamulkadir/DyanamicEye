# 0017 — Optional global shortcut

- Status: Accepted
- Date: 2026-10-26
- Task: P03-05

## Context
- Guide P03-05: a global shortcut that is optional and customizable; conflicts are detected; it works only if the OS
  grants the registration. AGENTS.md: keyboard accessible, nonintrusive, no bypassing of system controls.
- The island is keyboard reachable only after clicking it or through the tray (ADR 0009, 0012). A shortcut gives
  keyboard users a direct way in.
- A new setting is needed; ADR 0003 §7 allows an additive property with a default without a schema bump.

## Decision
1. **Off by default.** Settings → Behavior → "Global shortcut" check box plus a text field with the combination,
   default `Ctrl+Alt+I` (I for island). Nothing is registered until the user turns it on and saves.
2. **Format and rules** (`Core/Shell/Hotkey`): text such as `Ctrl+Alt+I`, parsed case-insensitively, saved in a
   canonical form (`Ctrl`, `Alt`, `Shift`, `Win` in that order, then the key). Keys: `A`–`Z`, `0`–`9`, `F1`–`F24`,
   `Space`. **At least two modifiers**, so the shortcut can never take over everyday single-modifier keys such as
   Ctrl+C or Alt+F (menus) in other apps. Invalid text shows a message and blocks Save (ADR 0014 validation).
   Stored as `behavior.shortcut` (`enabled`, `keys`); a damaged value falls back to the default combination.
3. **Registration only through `RegisterHotKey`** (`Platform.Windows/Input/Win32Hotkey`) on a message-only window,
   with `MOD_NOREPEAT`. No keyboard hooks, no polling. If Windows refuses (`ERROR_HOTKEY_ALREADY_REGISTERED` — another
   app or Windows itself owns it), the shortcut is **not** active and the settings page says "already used by another
   app or by Windows; choose another"; other failures say it could not be turned on. Conflicts with apps that read the
   keyboard in other ways cannot be detected; the manual matrix notes this.
4. **Action** (`Core/Shell/ShortcutAction`, pure): the shortcut opens or closes the island with keyboard focus.
   - Hidden → show and expand; Paused → resume and expand; Collapsed or Peek → expand;
   - Expanded or Pinned → collapse (Escape), returning focus to the previous app as before;
   - suspended for a fullscreen app → nothing (never pops over a game).
   When it expands, the island window is activated first (the process receiving `WM_HOTKEY` may take the foreground)
   so keyboard focus lands on the island.
5. **Lifecycle** (`App/ShortcutController`): registers after the window is shown, re-registers when the setting
   changes, unregisters on exit. In Safe Mode it still works (core keyboard access, not an optional component).
6. Logs: registered, failed with the Win32 error code, unregistered — never the key combination (it is a user choice,
   not needed for support).

## Alternatives considered
- Low-level keyboard hook — rejected: intrusive, sees every keystroke, often blocked by security software.
- A single-modifier default like `Win+I` or `Ctrl+Space` — rejected: already used by Windows or common apps.
- A key-recorder control that captures key presses — deferred: it swallows Tab and Esc, which hurts keyboard
  navigation; typed text with validation is accessible and simple.

## Consequences
- P05 accessibility review covers the shortcut field and status text.
- Manual rows SC1–SC4 in `docs/TEST_MATRIX.md`.
