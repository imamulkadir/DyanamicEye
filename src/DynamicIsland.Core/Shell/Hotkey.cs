using System.Globalization;

namespace DynamicIsland.Core.Shell;

[Flags]
public enum HotkeyModifiers
{
    None = 0,

    // Values are the Win32 MOD_* flags, so they can be passed to RegisterHotKey unchanged.
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// A global shortcut such as <c>Ctrl+Alt+I</c> (ADR 0017). Keys: A–Z, 0–9, F1–F24, Space; at least two modifiers, so a
/// shortcut never takes over everyday keys like Ctrl+C or Alt+F in other apps.
/// </summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, string Key)
{
    public const int MinimumModifiers = 2;

    private static readonly (HotkeyModifiers Flag, string Name)[] ModifierNames =
        [(HotkeyModifiers.Ctrl, "Ctrl"), (HotkeyModifiers.Alt, "Alt"), (HotkeyModifiers.Shift, "Shift"), (HotkeyModifiers.Win, "Win")];

    /// <summary>The Win32 virtual-key code of <see cref="Key"/>.</summary>
    public int VirtualKey => Key switch
    {
        "Space" => 0x20,
        [var c] when c is >= 'A' and <= 'Z' or >= '0' and <= '9' => c,
        _ => 0x70 + int.Parse(Key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture) - 1, // F1 = 0x70
    };

    /// <summary>Canonical text: modifiers in the order Ctrl, Alt, Shift, Win, then the key.</summary>
    public override string ToString() =>
        string.Join('+', ModifierNames.Where(m => Modifiers.HasFlag(m.Flag)).Select(m => m.Name).Append(Key));

    /// <returns><c>true</c> with the shortcut, or <c>false</c> with a message for the user.</returns>
    public static bool TryParse(string? text, out Hotkey? hotkey, out string? error)
    {
        hotkey = null;
        var parts = (text ?? string.Empty).Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(p => p.Length == 0))
        {
            error = "Enter modifiers and a key, for example Ctrl+Alt+I.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = ParseModifier(part);
            if (modifier == HotkeyModifiers.None)
            {
                error = $"\"{part}\" is not a modifier. Use Ctrl, Alt, Shift or Win.";
                return false;
            }

            if (modifiers.HasFlag(modifier))
            {
                error = $"{part} appears twice.";
                return false;
            }

            modifiers |= modifier;
        }

        var key = NormalizeKey(parts[^1]);
        if (key is null)
        {
            error = "The last key must be a letter, a digit, F1–F24 or Space.";
            return false;
        }

        if (System.Numerics.BitOperations.PopCount((uint)modifiers) < MinimumModifiers)
        {
            error = "Use at least two of Ctrl, Alt, Shift and Win, so the shortcut doesn't replace keys other apps use.";
            return false;
        }

        hotkey = new Hotkey(modifiers, key);
        error = null;
        return true;
    }

    private static HotkeyModifiers ParseModifier(string text) => text.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => HotkeyModifiers.Ctrl,
        "ALT" => HotkeyModifiers.Alt,
        "SHIFT" => HotkeyModifiers.Shift,
        "WIN" or "WINDOWS" => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    private static string? NormalizeKey(string text)
    {
        var upper = text.ToUpperInvariant();
        if (upper == "SPACE")
        {
            return "Space";
        }

        if (upper is [var c] && (c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
        {
            return upper;
        }

        return upper.Length is 2 or 3 && upper[0] == 'F'
               && int.TryParse(upper.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 24
               && upper[1] != '0'
            ? "F" + n.ToString(CultureInfo.InvariantCulture)
            : null;
    }
}

/// <summary>What the global shortcut does in each island state (ADR 0017 §4).</summary>
public static class ShortcutAction
{
    /// <returns>Inputs to fire in order (empty: do nothing).</returns>
    public static IReadOnlyList<IslandInput> For(IslandState state) => state switch
    {
        IslandState.Hidden => [IslandInput.Show, IslandInput.Click],
        IslandState.Paused => [IslandInput.Resume, IslandInput.Click],
        IslandState.Collapsed or IslandState.Peek => [IslandInput.Click],
        IslandState.Expanded or IslandState.Pinned => [IslandInput.Escape],
        _ => [],
    };

    /// <summary>True when the shortcut opens the island (the window should take keyboard focus first).</summary>
    public static bool Opens(IslandState state) => For(state).Contains(IslandInput.Click);
}
