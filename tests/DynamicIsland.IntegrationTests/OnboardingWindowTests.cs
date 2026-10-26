using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DynamicIsland.App;
using DynamicIsland.Core.Settings;
using DynamicIsland.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.IntegrationTests;

/// <summary>The real first-run window (P03-04, ADR 0015 §5) on an offscreen STA window.</summary>
public sealed class OnboardingWindowTests : IDisposable
{
    private static readonly string[] InputNames = ["StartupBox", "UpdatesBox", "ContinueButton"];

    private readonly StaDispatcherThread _ui = new();
    private readonly RecordingSettings _settings = new();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public void Consent_boxes_start_unchecked_and_every_widget_is_listed()
    {
        var (startup, updates, widgets) = _ui.Invoke(() =>
        {
            var window = Open();
            var result = (((CheckBox)window.FindName("StartupBox")).IsChecked, ((CheckBox)window.FindName("UpdatesBox")).IsChecked,
                ((ItemsControl)window.FindName("WidgetList")).Items.Count);
            window.Close();
            return result;
        });

        Assert.False(startup);
        Assert.False(updates);
        Assert.Equal(WidgetIds.All.Count, widgets);
    }

    [Fact]
    public async Task Continue_saves_explicit_choices_and_closes()
    {
        var window = _ui.Invoke(Open);
        _ui.Invoke(() => ((CheckBox)window.FindName("StartupBox")).IsChecked = true);

        Assert.True(await _ui.Invoke(window.ContinueAsync));

        Assert.Equal(ConsentState.Granted, _settings.Current.General.StartWithWindows);
        Assert.Equal(ConsentState.Declined, _settings.Current.Updates.AutomaticChecks);
        Assert.True(_ui.Invoke(() => window.Completed && !window.IsVisible));
    }

    [Fact]
    public void Closing_without_continue_saves_nothing()
    {
        _ui.Invoke(() => Open().Close());

        Assert.Equal(0, _settings.Updates);
        Assert.Equal(ConsentState.NotAsked, _settings.Current.General.StartWithWindows);
    }

    [Fact]
    public async Task Failed_save_keeps_the_window_open_with_a_message()
    {
        _settings.Failure = new IOException("denied");
        var window = _ui.Invoke(Open);

        Assert.False(await _ui.Invoke(window.ContinueAsync));

        Assert.True(_ui.Invoke(() => window.IsVisible));
        Assert.Contains("could not be saved", _ui.Invoke(() => ((TextBlock)window.FindName("StatusText")).Text), StringComparison.Ordinal);
        _ui.Invoke(window.Close);
    }

    [Fact]
    public void Inputs_have_automation_ids()
    {
        var ids = _ui.Invoke(() =>
        {
            var window = Open();
            var result = InputNames.Select(n => AutomationProperties.GetAutomationId((DependencyObject)window.FindName(n))).ToArray();
            window.Close();
            return result;
        });

        Assert.Equal(["Onboarding.Startup", "Onboarding.Updates", "Onboarding.Continue"], ids);
    }

    private OnboardingWindow Open()
    {
        var window = new OnboardingWindow(_settings, NullLogger<OnboardingWindow>.Instance)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
        };
        window.Show();
        return window;
    }

    private sealed class RecordingSettings : ISettingsService
    {
        public event EventHandler<AppSettings>? Changed;

        public AppSettings Current { get; private set; } = new();

        public bool IsReadOnly => false;

        public int Updates { get; private set; }

        public Exception? Failure { get; set; }

        public async Task<SettingsSaveResult> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken)
        {
            await Task.Yield();
            var updated = change(Current);
            if (Failure is not null)
            {
                throw Failure;
            }

            Updates++;
            Current = updated;
            Changed?.Invoke(this, updated);
            return SettingsSaveResult.Saved;
        }
    }
}
