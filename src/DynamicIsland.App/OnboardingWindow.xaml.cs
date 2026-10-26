using System.IO;
using System.Windows;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// First-run choices (guide P03-04, ADR 0015 §5): sign-in startup and daily update checks (both unchecked until the user
/// ticks them), a privacy summary and the widget choice. Continue saves; closing without it saves nothing.
/// </summary>
public partial class OnboardingWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly ILogger _logger;
    private readonly WidgetOption[] _widgets;

    public OnboardingWindow(ISettingsService settings, ILogger<OnboardingWindow> logger)
    {
        _settings = settings;
        _logger = logger;
        _widgets = [.. settings.Current.Widgets.Select(w => new WidgetOption(w.Id, SettingsEditor.WidgetLabel(w.Id), w.Enabled))];

        InitializeComponent();
        WidgetList.ItemsSource = _widgets;
        Loaded += (_, _) => StartupBox.Focus();
    }

    /// <summary>True once the choices were saved (tests).</summary>
    public bool Completed { get; private set; }

    /// <summary>Saves the shown choices; false (with a message in the window) when saving failed.</summary>
    public async Task<bool> ContinueAsync()
    {
        var startup = StartupBox.IsChecked == true;
        var updates = UpdatesBox.IsChecked == true;
        var widgets = _widgets.Select(w => new WidgetSetting(w.Id, w.Enabled)).ToArray();
        var enabledWidgets = widgets.Count(w => w.Enabled);

        ContinueButton.IsEnabled = false;
        try
        {
            await _settings.UpdateAsync(s => Onboarding.Apply(s, startup, updates, widgets), CancellationToken.None);
            AppLog.OnboardingCompleted(_logger, startup, updates, enabledWidgets);
            Completed = true;
            Close();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.SettingsNotSaved(_logger, ex.GetType().Name);
            StatusText.Text = "Your choices could not be saved. Check that the data folder is writable and try again.";
            return false;
        }
        finally
        {
            ContinueButton.IsEnabled = true;
        }
    }

    private async void OnContinue(object sender, RoutedEventArgs e) => await ContinueAsync();
}
