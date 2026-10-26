using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Core.Startup;

namespace DynamicIsland.UnitTests.Startup;

public sealed class StartupPolicyTests
{
    private const string Here = @"C:\Apps\DynamicIsland\DynamicIsland.Launcher.exe";
    private const string Elsewhere = @"D:\Portable\DynamicIsland.Launcher.exe";
    private static readonly string HereCommand = StartupPolicy.BuildCommand(Here);
    private static readonly string ElsewhereCommand = StartupPolicy.BuildCommand(Elsewhere);

    private static StartupRegistrationStatus Registered(string command, bool disabled = false, bool blocked = false) => new(command, disabled, blocked);

    [Fact]
    public void Command_quotes_the_launcher_and_marks_a_sign_in_start()
    {
        Assert.Equal($"\"{Here}\" --startup", HereCommand);
        Assert.Throws<ArgumentException>(() => StartupPolicy.BuildCommand("C:\\a\"b.exe"));
    }

    [Theory]
    [InlineData("\"C:\\A B\\x.exe\" --startup", "C:\\A B\\x.exe")]
    [InlineData("C:\\x.exe --startup", "C:\\x.exe")]
    [InlineData("C:\\x.exe", "C:\\x.exe")]
    [InlineData("\"unterminated", null)]
    [InlineData("  ", null)]
    public void Executable_is_read_from_a_run_value(string command, string? expected)
    {
        Assert.Equal(expected, StartupPolicy.ExecutableOf(command));
    }

    [Theory]
    [InlineData(ConsentState.NotAsked)]
    [InlineData(ConsentState.Declined)]
    public void Without_consent_an_entry_is_removed_and_nothing_is_added(ConsentState consent)
    {
        Assert.Equal(StartupAction.Unregister, StartupPolicy.Decide(consent, Registered(HereCommand), HereCommand, false, _ => true));
        Assert.Equal(StartupAction.Unregister, StartupPolicy.Decide(consent, Registered(ElsewhereCommand, disabled: true), HereCommand, false, _ => true));
        Assert.Equal(StartupAction.None, StartupPolicy.Decide(consent, StartupRegistrationStatus.None, HereCommand, false, _ => true));
    }

    [Fact]
    public void Consent_registers_this_copy_when_nothing_is_registered()
    {
        Assert.Equal(StartupAction.Register, StartupPolicy.Decide(ConsentState.Granted, StartupRegistrationStatus.None, HereCommand, false, _ => true));
        Assert.Equal(StartupAction.None, StartupPolicy.Decide(ConsentState.Granted, Registered(HereCommand), HereCommand, false, _ => true));
    }

    [Fact]
    public void Policy_block_and_missing_launcher_write_nothing()
    {
        Assert.Equal(StartupAction.None, StartupPolicy.Decide(ConsentState.Granted, new StartupRegistrationStatus(null, false, true), HereCommand, true, _ => true));
        Assert.Equal(StartupAction.None, StartupPolicy.Decide(ConsentState.Granted, StartupRegistrationStatus.None, null, true, _ => true));
    }

    [Fact]
    public void Disabled_in_windows_is_left_alone()
    {
        Assert.Equal(StartupAction.None, StartupPolicy.Decide(ConsentState.Granted, Registered(HereCommand, disabled: true), HereCommand, false, _ => true));
    }

    [Theory]
    [InlineData(false, true, StartupAction.None)] // Another copy that still exists keeps the entry (tests, second copies).
    [InlineData(false, false, StartupAction.Register)] // Moved or deleted portable copy: repair.
    [InlineData(true, true, StartupAction.Register)] // The user just switched it on in this copy.
    public void Entry_for_another_copy_is_taken_over_only_when_repaired_or_chosen(bool userJustEnabled, bool otherExists, StartupAction expected)
    {
        var checkedPaths = new List<string>();

        var action = StartupPolicy.Decide(ConsentState.Granted, Registered(ElsewhereCommand), HereCommand, userJustEnabled, p =>
        {
            checkedPaths.Add(p);
            return otherExists;
        });

        Assert.Equal(expected, action);
        Assert.All(checkedPaths, p => Assert.Equal(Elsewhere, p));
    }

    [Fact]
    public void Same_entry_differing_only_in_case_is_not_rewritten()
    {
        Assert.Equal(StartupAction.None, StartupPolicy.Decide(ConsentState.Granted, Registered(HereCommand.ToUpperInvariant()), HereCommand, false, _ => true));
    }

    [Theory]
    [InlineData(ConsentState.Declined, false, false, true, StartupState.Off)]
    [InlineData(ConsentState.Granted, false, false, true, StartupState.On)]
    [InlineData(ConsentState.Granted, true, false, true, StartupState.DisabledInWindows)]
    [InlineData(ConsentState.Granted, false, true, true, StartupState.BlockedByPolicy)]
    public void State_describes_what_windows_does(ConsentState consent, bool disabled, bool blocked, bool registered, StartupState expected)
    {
        var status = new StartupRegistrationStatus(registered ? HereCommand : null, disabled, blocked);

        Assert.Equal(expected, StartupPolicy.Describe(consent, status, HereCommand));
    }

    [Fact]
    public void State_reports_other_copy_missing_launcher_and_failed_registration()
    {
        Assert.Equal(StartupState.OtherCopy, StartupPolicy.Describe(ConsentState.Granted, Registered(ElsewhereCommand), HereCommand));
        Assert.Equal(StartupState.LauncherMissing, StartupPolicy.Describe(ConsentState.Granted, StartupRegistrationStatus.None, null));
        Assert.Equal(StartupState.NotRegistered, StartupPolicy.Describe(ConsentState.Granted, StartupRegistrationStatus.None, HereCommand));
    }
}

public sealed class StartupArgumentTests
{
    [Fact]
    public void App_recognizes_a_sign_in_start()
    {
        var parsed = LaunchArguments.Parse(["--STARTUP", "--safe-mode"]);

        Assert.True(parsed.Startup);
        Assert.True(parsed.SafeMode);
        Assert.Equal(0, parsed.IgnoredCount);
        Assert.False(LaunchArguments.Parse([]).Startup);
    }

    [Fact]
    public void Launcher_recognizes_a_sign_in_start_with_a_five_second_delay()
    {
        var parsed = LauncherArguments.Parse(["--startup"]);

        Assert.True(parsed.Startup);
        Assert.Equal(0, parsed.IgnoredCount);
        Assert.Equal(TimeSpan.FromSeconds(5), LauncherContract.StartupDelay);
    }
}

public sealed class OnboardingTests
{
    [Theory]
    [InlineData(ConsentState.NotAsked, ConsentState.Declined, false, false, true)]
    [InlineData(ConsentState.Granted, ConsentState.NotAsked, false, false, true)]
    [InlineData(ConsentState.Granted, ConsentState.Declined, false, false, false)]
    [InlineData(ConsentState.NotAsked, ConsentState.NotAsked, true, false, false)]
    [InlineData(ConsentState.NotAsked, ConsentState.NotAsked, false, true, false)]
    public void Shown_while_a_consent_is_unanswered_except_in_safe_mode_and_at_sign_in(
        ConsentState startup, ConsentState updates, bool safeMode, bool signIn, bool expected)
    {
        var settings = new AppSettings
        {
            General = new GeneralSettings { StartWithWindows = startup },
            Updates = new UpdateSettings { AutomaticChecks = updates },
        };

        Assert.Equal(expected, Onboarding.IsNeeded(settings, new LaunchArguments(safeMode, 0, signIn)));
    }

    [Fact]
    public void Unchecked_boxes_are_saved_as_explicit_no_and_other_settings_are_kept()
    {
        var latest = new AppSettings { General = new GeneralSettings { PreferredMonitorId = "m" } };
        WidgetSetting[] widgets = [.. WidgetIds.All.Select(id => new WidgetSetting(id, id != WidgetIds.Media))];

        var result = Onboarding.Apply(latest, startWithWindows: true, dailyUpdateChecks: false, widgets);

        Assert.Equal(ConsentState.Granted, result.General.StartWithWindows);
        Assert.Equal(ConsentState.Declined, result.Updates.AutomaticChecks);
        Assert.Equal("m", result.General.PreferredMonitorId);
        Assert.False(result.Widgets.Single(w => w.Id == WidgetIds.Media).Enabled);
    }
}
