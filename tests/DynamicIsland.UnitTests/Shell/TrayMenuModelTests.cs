using DynamicIsland.Core.Display;
using DynamicIsland.Core.Shell;

namespace DynamicIsland.UnitTests.Shell;

public sealed class TrayMenuModelTests
{
    private static readonly MonitorInfo Primary = new("primary-id", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1.0, IsPrimary: true);
    private static readonly MonitorInfo Left = new("left-id", new(-2560, 0, 2560, 1440), new(-2560, 0, 2560, 1400), 1.5, IsPrimary: false);

    [Fact]
    public void Menu_lists_every_tray_action_in_order()
    {
        var menu = TrayMenuModel.Build(IslandState.Collapsed, safeMode: false, [Primary], "primary-id");

        Assert.Equal(
            [
                TrayCommand.ToggleVisibility, TrayCommand.TogglePin, TrayCommand.TogglePause, TrayCommand.DisplayMenu, TrayCommand.Separator,
                TrayCommand.Settings, TrayCommand.CheckForUpdates, TrayCommand.OpenLogs, TrayCommand.Separator,
                TrayCommand.ToggleSafeMode, TrayCommand.Restart, TrayCommand.Exit,
            ],
            menu.Select(i => i.Command));
        Assert.All(menu.Where(i => i.Command != TrayCommand.Separator), i => Assert.StartsWith("Tray.", i.AutomationId, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(IslandState.Collapsed, "_Hide island", true, "P_ause")]
    [InlineData(IslandState.Hidden, "_Show island", true, "P_ause")]
    [InlineData(IslandState.Paused, "_Show island", false, "_Resume")]
    public void Visibility_and_pause_labels_follow_the_state(IslandState state, string toggleLabel, bool toggleEnabled, string pauseLabel)
    {
        var menu = TrayMenuModel.Build(state, safeMode: false, [Primary], null);

        var toggle = menu.Single(i => i.Command == TrayCommand.ToggleVisibility);
        Assert.Equal(toggleLabel, toggle.Label);
        Assert.Equal(toggleEnabled, toggle.Enabled);
        Assert.Equal(pauseLabel, menu.Single(i => i.Command == TrayCommand.TogglePause).Label);
    }

    [Theory]
    [InlineData(IslandState.Pinned, true, true)]
    [InlineData(IslandState.Expanded, true, false)]
    [InlineData(IslandState.Hidden, false, false)]
    [InlineData(IslandState.SuspendedFullscreen, false, false)]
    public void Pin_is_available_only_while_visible(IslandState state, bool enabled, bool isChecked)
    {
        var pin = TrayMenuModel.Build(state, safeMode: false, [Primary], null).Single(i => i.Command == TrayCommand.TogglePin);

        Assert.Equal(enabled, pin.Enabled);
        Assert.Equal(isChecked, pin.Checked);
    }

    [Fact]
    public void Displays_are_numbered_left_to_right_and_mark_primary_and_current()
    {
        var display = TrayMenuModel.Build(IslandState.Collapsed, safeMode: false, [Primary, Left], "PRIMARY-ID").Single(i => i.Command == TrayCommand.DisplayMenu);

        var children = display.Children!;
        Assert.Equal("Display _1", children[0].Label);
        Assert.Equal("left-id", children[0].Argument);
        Assert.False(children[0].Checked);
        Assert.Equal("Display _2 (primary)", children[1].Label);
        Assert.True(children[1].Checked);
        Assert.Equal(TrayCommand.Separator, children[2].Command);
        Assert.Equal(TrayCommand.ResetDisplay, children[3].Command);
        Assert.DoesNotContain(children, c => c.Label.Contains("-id", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, "Restart in Safe _Mode")]
    [InlineData(true, "_Leave Safe Mode (restart)")]
    public void Safe_mode_entry_reflects_the_current_mode(bool safeMode, string label)
    {
        Assert.Equal(label, TrayMenuModel.Build(IslandState.Collapsed, safeMode, [Primary], null).Single(i => i.Command == TrayCommand.ToggleSafeMode).Label);
    }

    [Fact]
    public void Features_from_later_tasks_are_disabled_with_an_explanation()
    {
        var menu = TrayMenuModel.Build(IslandState.Collapsed, safeMode: false, [Primary], null);

        foreach (var command in new[] { TrayCommand.Settings, TrayCommand.CheckForUpdates })
        {
            var item = menu.Single(i => i.Command == command);
            Assert.False(item.Enabled);
            Assert.False(string.IsNullOrWhiteSpace(item.ToolTip));
        }
    }
}
