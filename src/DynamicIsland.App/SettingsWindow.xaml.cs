using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Display;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DynamicIsland.App;

/// <summary>What the settings window shows about the app besides the settings themselves.</summary>
/// <param name="IsShuttingDown">True once the app is exiting: closing then never asks to save.</param>
public sealed record SettingsWindowContext(
    string Version,
    string Copyright,
    string DataFolder,
    string LogsFolder,
    Func<DiagnosticContext> Diagnostics,
    Func<bool> IsShuttingDown);

/// <summary>
/// Settings window (guide P03-03, ADR 0014): edits a <see cref="SettingsEditor"/> draft and saves it explicitly through
/// <see cref="ISettingsService"/>. Closing with unsaved changes asks first; closing never exits the app.
/// </summary>
public partial class SettingsWindow : Window
{
    public static readonly Uri ProjectPage = new("https://github.com/imamulkadir/DyanamicEye");

    private readonly ISettingsService _settings;
    private readonly DiagnosticBundleBuilder _diagnostics;
    private readonly SettingsWindowContext _context;
    private readonly ILogger _logger;
    private readonly SettingsEditor _editor;
    private DiagnosticPreview? _report;
    private bool _saving;
    private bool _closeConfirmed;

    public SettingsWindow(
        ISettingsService settings,
        IMonitorService monitors,
        DiagnosticBundleBuilder diagnostics,
        SettingsWindowContext context,
        ILogger<SettingsWindow> logger)
    {
        _settings = settings;
        _diagnostics = diagnostics;
        _context = context;
        _logger = logger;
        _editor = new SettingsEditor(settings.Current, monitors.GetMonitors(), CultureInfo.CurrentCulture);

        InitializeComponent();
        DataContext = _editor;

        ThemeBox.ItemsSource = new[] { Option(Core.Settings.ThemeMode.System, "Use Windows setting"), Option(Core.Settings.ThemeMode.Light, "Light"), Option(Core.Settings.ThemeMode.Dark, "Dark") };
        PlacementBox.ItemsSource = new[] { Option(IslandPlacement.TopCenter, "Top center"), Option(IslandPlacement.TopLeft, "Top left"), Option(IslandPlacement.TopRight, "Top right") };
        MotionBox.ItemsSource = new[]
        {
            Option(MotionPreference.System, "Use Windows animation setting"),
            Option(MotionPreference.Full, "Full"),
            Option(MotionPreference.Reduced, "Reduced"),
            Option(MotionPreference.Off, "Off"),
        };

        ReadOnlyBanner.Visibility = settings.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        UpdatesVersionText.Text = context.Version;
        AboutVersionText.Text = $"Version {context.Version}";
        AboutCopyrightText.Text = context.Copyright;
        DataFolderText.Text = context.DataFolder;
        ShowConsents();

        _editor.PropertyChanged += OnEditorChanged;
        _settings.Changed += OnSettingsChanged;
        Closed += (_, _) => _settings.Changed -= OnSettingsChanged;
        InputBindings.Add(new KeyBinding(new SaveCommand(this), Key.S, ModifierKeys.Control));
        Loaded += (_, _) => Pages.Focus();
    }

    /// <summary>The draft behind the window (tests).</summary>
    public SettingsEditor Editor => _editor;

    /// <summary>Saves the draft. Returns false when there is nothing valid to save or the save failed (shown in the window).</summary>
    public async Task<bool> SaveAsync()
    {
        if (_saving || !_editor.CanSave)
        {
            return !_editor.IsDirty;
        }

        _saving = true;
        try
        {
            var result = await _settings.UpdateAsync(_editor.CreateChange(), CancellationToken.None);
            _editor.Load(_settings.Current);
            AppLog.SettingsSaved(_logger, result);
            StatusText.Text = result == SettingsSaveResult.Saved
                ? "Settings saved."
                : "Applied until Dynamic Island restarts (settings can't be saved on this PC).";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.SettingsNotSaved(_logger, ex.GetType().Name);
            StatusText.Text = "Settings could not be saved. Check that the data folder is writable and try again.";
            return false;
        }
        finally
        {
            _saving = false;
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || !_editor.IsDirty || _context.IsShuttingDown() || TestHooks.Get(TestHooks.NoDialogsVariable) == "1")
        {
            return;
        }

        e.Cancel = true;
        var message = _editor.HasErrors
            ? "Some values are not valid, so your changes can't be saved. Close and discard them?"
            : "Save your changes before closing?";
        var buttons = _editor.HasErrors ? MessageBoxButton.OKCancel : MessageBoxButton.YesNoCancel;
        var answer = MessageBox.Show(this, message, Title, buttons, MessageBoxImage.Question);
        if (answer is MessageBoxResult.Cancel or MessageBoxResult.None)
        {
            return;
        }

        if (answer == MessageBoxResult.Yes && !await SaveAsync())
        {
            return;
        }

        _closeConfirmed = true;
        Close();
    }

    private static Choice<T> Option<T>(T value, string label) => new(value, label);

    private void ShowConsents()
    {
        StartWithWindowsText.Text = _editor.StartWithWindows switch
        {
            ConsentState.Granted => "On. You can change this in first-run setup; a switch here arrives in a later update.",
            ConsentState.Declined => "Off. You can change this in first-run setup; a switch here arrives in a later update.",
            _ => "Not set up yet. Dynamic Island will ask you once during first-run setup; it never starts with Windows without your permission.",
        };
        UpdateChecksText.Text = _editor.AutomaticUpdateChecks switch
        {
            ConsentState.Granted => "On: Dynamic Island will check GitHub once a day when update checks arrive.",
            ConsentState.Declined => "Off: Dynamic Island will not check for updates automatically.",
            _ => "Not set up yet. You will be asked before any automatic check.",
        };
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsEditor.IsDirty) or "" && _editor.IsDirty)
        {
            StatusText.Text = _editor.HasErrors ? $"Fix the value marked on the {PageWithError()} page to save." : string.Empty;
        }

        if (e.PropertyName is "")
        {
            ShowConsents();
        }
    }

    private string PageWithError() =>
        _editor.HorizontalOffsetError is not null || _editor.VerticalOffsetError is not null ? "Appearance" : "Behavior";

    /// <summary>Changes made elsewhere (tray) are shown when the draft has no pending edits; Save never overwrites them (ADR 0014).</summary>
    private void OnSettingsChanged(object? sender, AppSettings settings) => Dispatcher.BeginInvoke(() =>
    {
        if (!_editor.IsDirty && !_saving)
        {
            _editor.Load(_settings.Current);
        }
    });

    private async void OnSave(object sender, RoutedEventArgs e) => await SaveAsync();

    private void OnRevert(object sender, RoutedEventArgs e)
    {
        _editor.Revert();
        StatusText.Text = "Changes discarded.";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnResetPage(object sender, RoutedEventArgs e)
    {
        _editor.ResetSection(Enum.Parse<SettingsSection>((string)((FrameworkElement)sender).Tag));
        StatusText.Text = "Defaults filled in. Select Save to apply them.";
    }

    private void OnResetAll(object sender, RoutedEventArgs e)
    {
        var answer = TestHooks.Get(TestHooks.NoDialogsVariable) == "1"
            ? MessageBoxResult.OK
            : MessageBox.Show(this, "Fill in the defaults for all settings? Nothing changes until you select Save.", Title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK)
        {
            _editor.ResetAll();
            StatusText.Text = "Defaults filled in for all pages. Select Save to apply them.";
        }
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveSelectedWidget(-1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveSelectedWidget(1);

    private void MoveSelectedWidget(int delta)
    {
        if (WidgetList.SelectedIndex < 0)
        {
            StatusText.Text = "Select a widget first.";
            return;
        }

        WidgetList.SelectedIndex = _editor.MoveWidget(WidgetList.SelectedIndex, delta);
        WidgetList.ScrollIntoView(WidgetList.SelectedItem);
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e) => OpenFolder(_context.LogsFolder);

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => OpenFolder(_context.DataFolder);

    private void OpenFolder(string folder)
    {
        if (ShellLauncher.TryOpenFolder(folder) is { } errorType)
        {
            AppLog.OpenFolderFailed(_logger, errorType);
            StatusText.Text = "The folder could not be opened.";
        }
    }

    private void OnProjectLink(object sender, RoutedEventArgs e)
    {
        if (ShellLauncher.TryOpenLink(ProjectPage) is { } errorType)
        {
            AppLog.OpenFolderFailed(_logger, errorType);
            StatusText.Text = $"The browser could not be opened. The project page is {ProjectPage}";
        }
    }

    /// <summary>Builds the in-memory preview (ADR 0007); nothing is written until the user saves it.</summary>
    private async void OnPrepareReport(object sender, RoutedEventArgs e)
    {
        PrepareReportButton.IsEnabled = false;
        ReportSummaryText.Text = "Preparing…";
        try
        {
            var context = _context.Diagnostics();
            _report = await Task.Run(() => _diagnostics.BuildPreview(context));
            ReportFiles.ItemsSource = _report.Files.Select(f => new ReportFileItem(f, $"{f.Name} ({FormatSize(f.Bytes)})")).ToArray();
            ReportFiles.SelectedIndex = 0;
            ReportPreview.Visibility = Visibility.Visible;
            SaveReportButton.IsEnabled = true;
            ReportSummaryText.Text = $"{_report.Files.Count} files, {FormatSize(_report.TotalBytes)} in total. Review them below, then select Save report.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.DiagnosticReportFailed(_logger, ex.GetType().Name);
            ReportSummaryText.Text = "The report could not be prepared.";
        }
        finally
        {
            PrepareReportButton.IsEnabled = true;
        }
    }

    private void OnReportFileSelected(object sender, SelectionChangedEventArgs e) =>
        ReportContent.Text = (ReportFiles.SelectedItem as ReportFileItem)?.File.Content ?? string.Empty;

    private async void OnSaveReport(object sender, RoutedEventArgs e)
    {
        if (_report is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save diagnostic report",
            FileName = $"dynamicisland-diagnostics-{_report.CreatedUtc:yyyyMMdd-HHmmss}.zip",
            DefaultExt = ".zip",
            Filter = "Zip archive (*.zip)|*.zip",
            OverwritePrompt = false,
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await DiagnosticBundleBuilder.WriteZipAsync(_report, dialog.FileName, CancellationToken.None);
            AppLog.DiagnosticReportSaved(_logger, _report.Files.Count, _report.TotalBytes);
            ReportSummaryText.Text = "Report saved. Attach it to a GitHub bug report yourself if you want to share it.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.DiagnosticReportFailed(_logger, ex.GetType().Name);
            ReportSummaryText.Text = File.Exists(dialog.FileName)
                ? "A file with that name already exists. Choose another name."
                : "The report could not be saved there. Choose another folder.";
        }
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} bytes" : $"{(bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture)} KB";

    private sealed record Choice<T>(T Value, string Label);

    private sealed record ReportFileItem(DiagnosticFile File, string Label);

    private sealed class SaveCommand(SettingsWindow window) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public async void Execute(object? parameter) => await window.SaveAsync();
    }
}
