using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DynamicIsland.App;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Display;
using DynamicIsland.Core.PersonalData;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Startup;
using DynamicIsland.Infrastructure.Diagnostics;
using DynamicIsland.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynamicIsland.IntegrationTests;

/// <summary>The real settings window (P03-03, ADR 0014) on an offscreen STA window with in-memory settings.</summary>
public sealed class SettingsWindowTests : IDisposable
{
    private static readonly MonitorInfo Primary = new("primary-id", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1.0, IsPrimary: true);
    private static readonly MonitorInfo Second = new("second-id", new(1920, 0, 1920, 1080), new(1920, 0, 1920, 1040), 1.0, IsPrimary: false);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "DynamicIsland.IntegrationTests", Guid.NewGuid().ToString("N"));
    private readonly StaDispatcherThread _ui = new();
    private readonly FakeSettingsService _settings = new();
    private StartupState _startupState = StartupState.Off;
    private ShortcutState _shortcutState = ShortcutState.Off;
    private Core.Distribution.UpdatePolicy _updatePolicy = Core.Distribution.UpdatePolicy.Allowed;

    public void Dispose()
    {
        _ui.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Window_has_every_page_and_accessible_controls()
    {
        var pages = _ui.Invoke(() =>
        {
            var window = Open();
            var tabs = (TabControl)window.FindName("Pages");
            var result = tabs.Items.Cast<TabItem>().Select(t => $"{t.Header}|{AutomationProperties.GetAutomationId(t)}").ToArray();
            window.Close();
            return result;
        });

        Assert.Equal(
            ["General", "Appearance", "Behavior", "Widgets", "Updates", "Privacy", "Diagnostics", "About"],
            pages.Select(p => p.Split('|')[0]));
        Assert.All(pages, p => Assert.StartsWith("Settings.Page.", p.Split('|')[1], StringComparison.Ordinal));
    }

    [Fact]
    public void Every_input_has_an_automation_id_and_a_label_or_name()
    {
        var unnamed = _ui.Invoke(() =>
        {
            var window = Open();
            var tabs = (TabControl)window.FindName("Pages");
            var missing = new List<string>();
            foreach (TabItem tab in tabs.Items)
            {
                tabs.SelectedItem = tab;
                window.UpdateLayout();
                foreach (var control in Descendants(window).OfType<Control>().Where(c => c is TextBox or ComboBox or Slider or CheckBox or ButtonBase or ListBox))
                {
                    if (control.TemplatedParent is not null)
                    {
                        continue; // Parts of a control template (scroll bars, toggle buttons).
                    }

                    var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(control);
                    if (string.IsNullOrEmpty(AutomationProperties.GetAutomationId(control)) || string.IsNullOrWhiteSpace(peer?.GetName()))
                    {
                        missing.Add($"{tab.Header}: {control.GetType().Name} '{(control as ContentControl)?.Content}'");
                    }
                }
            }

            window.Close();
            return missing;
        });

        Assert.True(unnamed.Count == 0, string.Join("; ", unnamed));
    }

    [Fact]
    public async Task Save_writes_only_the_changed_value_and_keeps_a_concurrent_tray_change()
    {
        var window = _ui.Invoke(Open);
        _ui.Invoke(() => window.Editor.Opacity = 0.5);

        // The tray picks another display while the window has unsaved edits.
        await _settings.UpdateAsync(s => s with { General = s.General with { PreferredMonitorId = "second-id" } }, CancellationToken.None);
        Assert.True(_ui.Invoke(() => window.Editor.IsDirty));

        Assert.True(await _ui.Invoke(window.SaveAsync));

        Assert.Equal(0.5, _settings.Current.Appearance.Opacity);
        Assert.Equal("second-id", _settings.Current.General.PreferredMonitorId);
        Assert.False(_ui.Invoke(() => window.Editor.IsDirty));
        Assert.Equal("Settings saved.", _ui.Invoke(() => ((TextBlock)window.FindName("StatusText")).Text));
        _ui.Invoke(window.Close);
    }

    [Fact]
    public async Task External_change_reloads_a_window_without_edits()
    {
        var window = _ui.Invoke(Open);

        await _settings.UpdateAsync(s => s with { General = s.General with { PreferredMonitorId = "second-id" } }, CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_ui.Invoke(() => window.Editor.SelectedDisplay.Id) != "second-id" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal("second-id", _ui.Invoke(() => window.Editor.SelectedDisplay.Id));
        Assert.False(_ui.Invoke(() => window.Editor.IsDirty));
        _ui.Invoke(window.Close);
    }

    [Fact]
    public void Invalid_input_disables_save_and_shows_the_message()
    {
        var (canSave, saveEnabled, error) = _ui.Invoke(() =>
        {
            var window = Open();
            ((TextBox)window.FindName("HoverDelayBox")).Text = "5";
            window.UpdateLayout();
            var result = (window.Editor.CanSave, ((Button)window.FindName("SaveButton")).IsEnabled,
                AutomationProperties.GetHelpText((TextBox)window.FindName("HoverDelayBox")) + "|" + ((TextBlock)window.FindName("StatusText")).Text);
            window.Editor.Revert();
            window.Close();
            return result;
        });

        Assert.False(canSave);
        Assert.False(saveEnabled);
        Assert.Contains("100 to 2000", error, StringComparison.Ordinal);
        Assert.Contains("Behavior page", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_save_keeps_the_draft_and_explains()
    {
        _settings.Failure = new IOException("disk full");
        var window = _ui.Invoke(Open);
        _ui.Invoke(() => window.Editor.HoverPreviewEnabled = false);

        Assert.False(await _ui.Invoke(window.SaveAsync));

        Assert.True(_ui.Invoke(() => window.Editor.IsDirty));
        Assert.Contains("could not be saved", _ui.Invoke(() => ((TextBlock)window.FindName("StatusText")).Text), StringComparison.Ordinal);
        Assert.True(_settings.Current.Behavior.HoverPreviewEnabled);
        _ui.Invoke(() => window.Editor.Revert());
        _ui.Invoke(window.Close);
    }

    [Fact]
    public async Task Startup_box_saves_an_explicit_choice_and_shows_what_windows_does()
    {
        _startupState = StartupState.DisabledInWindows;
        var window = _ui.Invoke(Open);
        Assert.Contains("Settings › Apps › Startup", _ui.Invoke(() => ((TextBlock)window.FindName("StartupStatusText")).Text), StringComparison.Ordinal);

        _ui.Invoke(() => window.Editor.StartWithWindows = true);
        Assert.True(await _ui.Invoke(window.SaveAsync));

        Assert.Equal(ConsentState.Granted, _settings.Current.General.StartWithWindows);
        Assert.Equal(ConsentState.NotAsked, _settings.Current.Updates.AutomaticChecks);
        _ui.Invoke(window.Close);
    }

    [Fact]
    public async Task Shortcut_is_validated_saved_canonically_and_its_conflict_is_explained()
    {
        _shortcutState = ShortcutState.InUse;
        var window = _ui.Invoke(Open);
        Assert.Contains("another app or Windows", _ui.Invoke(() => ((TextBlock)window.FindName("ShortcutStatusText")).Text), StringComparison.Ordinal);

        _ui.Invoke(() =>
        {
            window.Editor.ShortcutEnabled = true;
            window.Editor.ShortcutText = "ctrl+i";
        });
        Assert.False(_ui.Invoke(() => window.Editor.CanSave));
        Assert.NotNull(_ui.Invoke(() => window.Editor.ShortcutError));

        _ui.Invoke(() => window.Editor.ShortcutText = "alt + ctrl + f9");
        Assert.True(await _ui.Invoke(window.SaveAsync));

        Assert.Equal(new ShortcutSettings { Enabled = true, Keys = "Ctrl+Alt+F9" }, _settings.Current.Behavior.Shortcut);
        _ui.Invoke(window.Close);
    }

    [Fact]
    public void Read_only_store_shows_the_banner()
    {
        _settings.IsReadOnly = true;

        var visibility = _ui.Invoke(() =>
        {
            var window = Open();
            var result = ((FrameworkElement)window.FindName("ReadOnlyBanner")).Visibility;
            window.Close();
            return result;
        });

        Assert.Equal(Visibility.Visible, visibility);
    }

    [Fact]
    public void Display_list_offers_primary_and_each_display()
    {
        var labels = _ui.Invoke(() =>
        {
            var window = Open();
            var result = ((ComboBox)window.FindName("DisplayBox")).Items.Cast<object>().Select(o => ((Core.Shell.DisplayOption)o).Label).ToArray();
            window.Close();
            return result;
        });

        Assert.Equal(["Primary display (follows Windows)", "Display 1 (primary)", "Display 2"], labels);
    }

    [Fact]
    public async Task Diagnostic_report_is_previewed_before_anything_is_written()
    {
        var window = _ui.Invoke(Open);

        _ui.Invoke(() => ((Button)window.FindName("PrepareReportButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!_ui.Invoke(() => ((Button)window.FindName("SaveReportButton")).IsEnabled) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        var files = _ui.Invoke(() => ((ListBox)window.FindName("ReportFiles")).Items.Count);
        var content = _ui.Invoke(() => ((TextBox)window.FindName("ReportContent")).Text);

        Assert.Equal(3, files);
        Assert.Contains("1.2.3", content, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_root) && Directory.EnumerateFiles(_root, "*.zip", SearchOption.AllDirectories).Any());
        _ui.Invoke(window.Close);
    }

    [Theory]
    [InlineData(Core.Distribution.UpdatePolicySource.MachinePolicy, "your organization")]
    [InlineData(Core.Distribution.UpdatePolicySource.InstallerFlag, "whoever installed this copy")]
    public void An_it_managed_copy_says_so_and_locks_the_daily_check(Core.Distribution.UpdatePolicySource source, string expected)
    {
        _updatePolicy = new Core.Distribution.UpdatePolicy(true, source);
        var window = _ui.Invoke(Open);

        var (text, visible, checkEnabled) = _ui.Invoke(() =>
        {
            var note = (TextBlock)window.FindName("ManagedUpdatesText");
            return (note.Text, note.Visibility, ((CheckBox)window.FindName("UpdateChecksBox")).IsEnabled);
        });

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Equal(Visibility.Visible, visible);
        Assert.False(checkEnabled);
        _ui.Invoke(window.Close);
    }

    [Fact]
    public void An_unmanaged_copy_shows_no_note()
    {
        var window = _ui.Invoke(Open);

        var (visible, checkEnabled) = _ui.Invoke(() =>
            (((TextBlock)window.FindName("ManagedUpdatesText")).Visibility, ((CheckBox)window.FindName("UpdateChecksBox")).IsEnabled));

        Assert.Equal((Visibility.Collapsed, true), (visible, checkEnabled));
        _ui.Invoke(window.Close);
    }

    private SettingsWindow Open()
    {
        var context = new SettingsWindowContext(
            "1.2.3",
            "Copyright test",
            _root,
            Path.Combine(_root, "logs"),
            () => new DiagnosticContext("1.2.3", SafeMode: false, "Loaded", []),
            () => false,
            () => _startupState,
            () => _shortcutState,
            new NoBackup(),
            () => false,
            _updatePolicy);
        var builder = new DiagnosticBundleBuilder(Path.Combine(_root, "logs"), Path.Combine(_root, "data"), new LogRedactor([]), TimeProvider.System);
        var window = new SettingsWindow(_settings, new FixedMonitors([Primary, Second]), builder, context, NullLogger<SettingsWindow>.Instance)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
        };
        window.Show();
        return window;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class FixedMonitors(IReadOnlyList<MonitorInfo> monitors) : IMonitorService
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => monitors;
    }

    private sealed class NoBackup : IDataBackupService
    {
        public Task<BackupExportResult> ExportAsync(string destinationPath, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BackupPreview> PreviewAsync(string archivePath, CancellationToken cancellationToken) => Task.FromResult(BackupPreview.Refused(BackupProblem.NotAnArchive));

        public Task<bool> ScheduleRestoreAsync(BackupPreview approved, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public event EventHandler<AppSettings>? Changed;

        public AppSettings Current { get; private set; } = new();

        public bool IsReadOnly { get; set; }

        public Exception? Failure { get; set; }

        public async Task<SettingsSaveResult> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken)
        {
            // Like SettingsService: the change may run off the caller's thread.
            await Task.Yield();
            var updated = SettingsValidator.Normalize(change(Current)).Settings;
            if (Failure is not null)
            {
                throw Failure;
            }

            Current = updated;

            Changed?.Invoke(this, Current);
            return IsReadOnly ? SettingsSaveResult.SkippedReadOnly : SettingsSaveResult.Saved;
        }
    }
}
