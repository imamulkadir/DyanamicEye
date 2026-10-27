using System.Windows;
using DynamicIsland.Core.Updates;

namespace DynamicIsland.App;

/// <summary>
/// Opens the update window for a release, or brings the open one to the front (one at a time). The steps for a release
/// live as long as the app, so a package downloaded and then postponed with Later is still ready when the window
/// reopens (ADR 0033); a newer release starts over.
/// </summary>
public sealed class UpdateWindowService(Func<ReleaseInfo, UpdateFlow> createFlow, Func<UpdateFlow, UpdateWindow> createWindow)
{
    private UpdateFlow? _flow;
    private UpdateWindow? _window;

    /// <summary>The open window, if any (tests and diagnostics).</summary>
    public UpdateWindow? Window => _window;

    public void Show(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (_flow is null || _flow.Release.Version != release.Version)
        {
            _window?.Close();
            _flow?.Dispose();
            _flow = createFlow(release);
        }

        if (_window is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return;
        }

        var window = createWindow(_flow);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };
        _window = window;
        window.Show();
        window.Activate();
    }
}
