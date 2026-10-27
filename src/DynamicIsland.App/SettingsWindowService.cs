using System.Windows;

namespace DynamicIsland.App;

/// <summary>Opens the settings window, or brings the open one to the front (one window at a time, ADR 0014).</summary>
public sealed class SettingsWindowService(Func<SettingsWindow> create)
{
    private SettingsWindow? _window;

    /// <summary>The open window, if any (tests and diagnostics).</summary>
    public SettingsWindow? Window => _window;

    public void Show()
    {
        if (_window is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return;
        }

        var window = create();
        window.Closed += (_, _) => _window = null;
        _window = window;
        window.Show();
        window.Activate();
    }

    /// <summary>Tray "Check for updates": the Updates page, checking right away (ADR 0032).</summary>
    public void ShowAndCheckForUpdates()
    {
        Show();
        _window?.CheckForUpdatesNow();
    }
}
