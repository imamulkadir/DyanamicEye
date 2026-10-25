using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// WPF entry point. Owns the Generic Host lifetime; all composition lives in <see cref="AppHost"/>.
/// The host (and with it the file logger) is built before any window is shown.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(5);

    private IHost? _host;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = AppHost.CreateBuilder().Build();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var architecture = RuntimeInformation.OSArchitecture.ToString();
        AppLog.Started(_logger, version, Environment.OSVersion.VersionString, architecture);

        _host.Start();

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.Exiting(_logger, e.ApplicationExitCode);
        }

        if (_host is not null)
        {
            // Stop off the UI thread so hosted services awaiting the dispatcher cannot deadlock shutdown.
            // Disposing the host flushes the file logger.
            Task.Run(() => _host.StopAsync(HostStopTimeout)).GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }

        base.OnExit(e);
    }
}
