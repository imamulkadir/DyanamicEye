using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DynamicIsland.App;

/// <summary>
/// WPF entry point. Owns the Generic Host lifetime; all composition lives in <see cref="AppHost"/>.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(5);

    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = AppHost.CreateBuilder().Build();
        _host.Start();

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // Stop off the UI thread so hosted services awaiting the dispatcher cannot deadlock shutdown.
            Task.Run(() => _host.StopAsync(HostStopTimeout)).GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }

        base.OnExit(e);
    }
}
