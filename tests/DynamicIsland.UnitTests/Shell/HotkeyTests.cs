using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;

namespace DynamicIsland.UnitTests.Shell;

public sealed class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+I", "Ctrl+Alt+I", 0x49)]
    [InlineData("alt + ctrl + i", "Ctrl+Alt+I", 0x49)]
    [InlineData("Win+Shift+Space", "Shift+Win+Space", 0x20)]
    [InlineData("Control+Shift+7", "Ctrl+Shift+7", 0x37)]
    [InlineData("Ctrl+Alt+f1", "Ctrl+Alt+F1", 0x70)]
    [InlineData("Ctrl+Alt+F24", "Ctrl+Alt+F24", 0x87)]
    public void Valid_shortcuts_are_parsed_to_canonical_text_and_virtual_keys(string text, string canonical, int virtualKey)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey, out var error));

        Assert.Null(error);
        Assert.Equal(canonical, hotkey!.ToString());
        Assert.Equal(virtualKey, hotkey.VirtualKey);
    }

    [Fact]
    public void Modifier_flags_match_the_win32_values()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Alt+Shift+Win+A", out var hotkey, out _));

        Assert.Equal(0x1 | 0x2 | 0x4 | 0x8, (int)hotkey!.Modifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("I")]
    [InlineData("Ctrl+C")] // One modifier would take over Ctrl+C in every app.
    [InlineData("Alt+F")] // ... or the File menu.
    [InlineData("Ctrl+Alt+")]
    [InlineData("Ctrl+Alt+Esc")]
    [InlineData("Ctrl+Alt+F25")]
    [InlineData("Ctrl+Alt+F0")]
    [InlineData("Ctrl+Alt+F01")]
    [InlineData("Ctrl+Ctrl+I")]
    [InlineData("Ctrl+Hyper+I")]
    [InlineData("Ctrl+Alt+II")]
    public void Invalid_shortcuts_explain_the_problem(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out var hotkey, out var error));

        Assert.Null(hotkey);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(IslandState.Hidden, new[] { IslandInput.Show, IslandInput.Click })]
    [InlineData(IslandState.Paused, new[] { IslandInput.Resume, IslandInput.Click })]
    [InlineData(IslandState.Collapsed, new[] { IslandInput.Click })]
    [InlineData(IslandState.Peek, new[] { IslandInput.Click })]
    [InlineData(IslandState.Expanded, new[] { IslandInput.Escape })]
    [InlineData(IslandState.Pinned, new[] { IslandInput.Escape })]
    [InlineData(IslandState.SuspendedFullscreen, new IslandInput[0])]
    public void Shortcut_opens_or_closes_the_island_but_never_over_a_fullscreen_app(IslandState state, IslandInput[] expected)
    {
        Assert.Equal(expected, ShortcutAction.For(state));
    }

    [Theory]
    [InlineData(IslandState.Hidden)]
    [InlineData(IslandState.Paused)]
    [InlineData(IslandState.Collapsed)]
    public void Opening_sequences_end_expanded(IslandState start)
    {
        var snapshot = IslandSnapshot.Initial with { State = start };
        foreach (var input in ShortcutAction.For(start))
        {
            snapshot = IslandTransitions.Next(snapshot, input);
        }

        Assert.Equal(IslandState.Expanded, snapshot.State);
        Assert.True(ShortcutAction.Opens(start));
    }

    [Fact]
    public void Shortcut_setting_is_off_by_default_and_damaged_keys_are_repaired()
    {
        var settings = new AppSettings();
        Assert.False(settings.Behavior.Shortcut.Enabled);
        Assert.Equal("Ctrl+Alt+I", settings.Behavior.Shortcut.Keys);

        var damaged = settings with { Behavior = settings.Behavior with { Shortcut = new ShortcutSettings { Enabled = true, Keys = "Ctrl+Q" } } };
        var lower = settings with { Behavior = settings.Behavior with { Shortcut = new ShortcutSettings { Keys = "alt+ctrl+k" } } };

        var repaired = SettingsValidator.Normalize(damaged);
        Assert.True(repaired.WasCorrected);
        Assert.Equal(new ShortcutSettings { Enabled = true, Keys = "Ctrl+Alt+I" }, repaired.Settings.Behavior.Shortcut);
        Assert.Equal("Ctrl+Alt+K", SettingsValidator.Normalize(lower).Settings.Behavior.Shortcut.Keys);
    }
}
