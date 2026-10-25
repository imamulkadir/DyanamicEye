using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core.Components;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;
using DynamicIsland.Platform.Windows.Instance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// WPF entry point and the only place that touches process-level events.
/// Startup order: single instance → host (logger) → session marker → settings → window.
/// Every exit path goes through <see cref="LifecycleCoordinator"/>; only fatal exits leave the session marked unhealthy.
/// </summary>
public partial class App : Application
{
    private const string AppId = "DynamicIsland";
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(5);

    private SingleInstanceGuard? _instance;
    private IHost? _host;
    private ILogger<App>? _logger;
    private LifecycleCoordinator? _lifecycle;
    private ISessionStateMarker? _session;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = SingleInstanceGuard.TryAcquire(AppId);
        if (_instance is null)
        {
            // Another instance owns this user session: ask it to show itself and leave quietly.
            Task.Run(() => SingleInstanceGuard.SignalExistingInstanceAsync(AppId, ActivationTimeout, CancellationToken.None)).GetAwaiter().GetResult();
            Shutdown(ExitCodes.Success);
            return;
        }

        var launch = LaunchArguments.Parse(e.Args);
        _host = AppHost.CreateBuilder(launchArguments: launch).Build();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        _lifecycle = _host.Services.GetRequiredService<LifecycleCoordinator>();
        _session = _host.Services.GetRequiredService<ISessionStateMarker>();
        RegisterUnhandledExceptionHandlers();

        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var architecture = RuntimeInformation.OSArchitecture.ToString();
        AppLog.Started(_logger, version, Environment.OSVersion.VersionString, architecture);
        if (launch.SafeMode)
        {
            // Nothing optional exists yet (widgets arrive in P04); Safe Mode will skip them and their providers.
            AppLog.SafeModeActive(_logger);
        }

        if (launch.IgnoredCount > 0)
        {
            AppLog.ArgumentsIgnored(_logger, launch.IgnoredCount);
        }

        if (_session.BeginSession())
        {
            AppLog.PreviousSessionUnclean(_logger);
        }

        _host.Start();
        _lifecycle.ShutdownRequested += OnShutdownRequested;

        var store = _host.Services.GetRequiredService<ISettingsStore>();
        var token = _lifecycle.ApplicationStopping;
        var settings = Task.Run(() => store.LoadAsync(token), token).GetAwaiter().GetResult();
        AppLog.SettingsLoaded(_logger, settings.Outcome, settings.Corrections.Count, store.IsReadOnly);

        var window = _host.Services.GetRequiredService<MainWindow>();
        if (launch.SafeMode)
        {
            window.Title += " (Safe Mode)";
        }

        MainWindow = window;
        window.Show();

        _instance.StartListening(OnActivationRequested, OnActivationUnavailable);

        // Optional components (widgets/providers, P04) run isolated; failures surface as status changes and notices.
        var logger = _logger;
        _host.Services.GetRequiredService<INotificationService>().Published +=
            (_, notice) => AppLog.NoticePublished(logger, notice.Severity, notice.MessageKey, notice.Subject);
        var components = _host.Services.GetRequiredService<ComponentHost>();
        components.StatusChanged +=
            (_, s) => AppLog.ComponentStatusChanged(logger, s.Name, s.State, s.ReasonCode, s.ConsecutiveFailures, s.LastErrorType);
        components.StartAll();

        ApplyTestCrashHook();
    }

    /// <summary>Debug-build fault injection for crash-recovery tests; <see cref="TestHooks"/> returns null in Release.</summary>
    private void ApplyTestCrashHook()
    {
        switch (TestHooks.Get(TestHooks.CrashVariable))
        {
            case "ui":
                Dispatcher.BeginInvoke(() => throw new InvalidOperationException("Injected test crash on the UI thread."));
                break;
            case "background":
                new Thread(() => throw new InvalidOperationException("Injected test crash on a background thread.")) { IsBackground = true }.Start();
                break;
            case "startup":
                throw new InvalidOperationException("Injected test crash during startup.");
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _lifecycle?.RequestShutdown(ExitReason.SessionEnding);
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_lifecycle is not null && _logger is not null)
        {
            _lifecycle.ShutdownRequested -= OnShutdownRequested;
            _lifecycle.RequestShutdown(ExitReason.UserExit);

            var report = Task.Run(_lifecycle.ShutdownAsync).GetAwaiter().GetResult();
            foreach (var step in report.Steps.Where(s => s.Status != ShutdownStepStatus.Completed))
            {
                AppLog.ShutdownStepIncomplete(_logger, step.Name, step.Status, (long)step.Elapsed.TotalMilliseconds, step.ErrorType);
            }

            AppLog.Exiting(_logger, report.Reason, report.ExitCode);
            if (report.Reason != ExitReason.Fatal)
            {
                _session?.EndSessionCleanly();
            }

            e.ApplicationExitCode = report.ExitCode;
        }

        if (_host is not null)
        {
            // Stop off the UI thread so hosted services awaiting the dispatcher cannot deadlock shutdown.
            // Disposing the host flushes the file logger.
            Task.Run(() => _host.StopAsync(HostStopTimeout)).GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }

        _instance?.Dispose();
        _instance = null;

        base.OnExit(e);
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.ShutdownRequested(_logger, e.Reason);
        }

        Dispatcher.BeginInvoke(() => Shutdown(e.ExitCode));
    }

    private void OnActivationRequested()
    {
        if (_logger is not null)
        {
            AppLog.ActivationRequested(_logger);
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is not { } window)
            {
                return;
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Show();
            window.Activate();
        });
    }

    private void OnActivationUnavailable(Exception exception)
    {
        if (_logger is not null)
        {
            AppLog.ActivationUnavailable(_logger, exception.GetType().Name);
        }
    }

    private void RegisterUnhandledExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// UI-thread failures are fatal. <c>Handled</c> is set only to run a controlled fatal shutdown (log, stop components,
    /// keep the session marked unhealthy, exit code 1); it never lets the app continue. A failure during that shutdown
    /// is left unhandled so the process terminates.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.UnhandledException(_logger, e.Exception, "UI thread");
        }

        e.Handled = _lifecycle?.RequestShutdown(ExitReason.Fatal) == true;
    }

    /// <summary>Background-thread failures terminate the process; flush logs so the cause is kept.</summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (_logger is null || _host is null)
        {
            return;
        }

        if (e.ExceptionObject is Exception exception)
        {
            AppLog.UnhandledException(_logger, exception, "background thread");
        }

        foreach (var provider in _host.Services.GetServices<ILoggerProvider>())
        {
            provider.Dispose();
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.UnobservedTaskException(_logger, e.Exception);
        }
    }
}
