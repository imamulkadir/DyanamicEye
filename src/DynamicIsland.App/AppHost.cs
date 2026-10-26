using DynamicIsland.Core.Components;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Display;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Shell;
using DynamicIsland.Core.Startup;
using DynamicIsland.Core.Widgets;
using DynamicIsland.Infrastructure;
using DynamicIsland.Infrastructure.Diagnostics;
using DynamicIsland.Infrastructure.Lifecycle;
using DynamicIsland.Infrastructure.Logging;
using DynamicIsland.Infrastructure.Settings;
using DynamicIsland.Platform.Windows.Display;
using DynamicIsland.Platform.Windows.Startup;
using DynamicIsland.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.App;

/// <summary>
/// Composition root. Builds the Generic Host without default sources: no command-line or environment
/// configuration (launch arguments are untrusted input) and no console/debug/EventLog log providers.
/// The only log sink is the local redacting file logger.
/// </summary>
public static class AppHost
{
    /// <param name="dataRoot">Overrides <c>%LOCALAPPDATA%\DynamicIsland</c> (tests); <c>null</c> in the app.</param>
    /// <param name="launchArguments">Parsed command line; defaults to a normal (non-Safe-Mode) start.</param>
    public static HostApplicationBuilder CreateBuilder(string? dataRoot = null, LaunchArguments? launchArguments = null)
    {
        var root = dataRoot ?? AppDataPaths.Root;

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ApplicationName = "DynamicIsland",
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);

        builder.Services.AddSingleton(launchArguments ?? LaunchArguments.Default);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(_ => LogRedactor.ForCurrentEnvironment());
        builder.Services.AddSingleton(_ => new FileLoggerOptions(System.IO.Path.Combine(root, AppDataPaths.LogsFolderName)));
        builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();

        var dataDirectory = System.IO.Path.Combine(root, AppDataPaths.DataFolderName);
        builder.Services.AddSingleton<LifecycleCoordinator>();
        builder.Services.AddSingleton<ISessionStateMarker>(sp =>
            new FileSessionStateMarker(dataDirectory, sp.GetRequiredService<ILogger<FileSessionStateMarker>>()));
        builder.Services.AddSingleton<INotificationService, InMemoryNotificationService>();
        builder.Services.AddSingleton<IFailureClassifier, DefaultFailureClassifier>();
        builder.Services.AddSingleton<ComponentHost>();
        builder.Services.AddSingleton(sp => new WidgetRegistry(
            sp.GetRequiredService<ComponentHost>(),
            BuiltInWidgets.Create(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<TimeProvider>())));
        builder.Services.AddSingleton(sp => new DiagnosticBundleBuilder(
            System.IO.Path.Combine(root, AppDataPaths.LogsFolderName),
            dataDirectory,
            sp.GetRequiredService<LogRedactor>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(_ => new SettingsStoreOptions(dataDirectory));
        builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();

        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        builder.Services.AddSingleton<IMonitorService, Win32MonitorService>();
        builder.Services.AddSingleton<IIslandStateMachine>(sp => new IslandStateMachine(
            sp.GetRequiredService<TimeProvider>(),
            action => System.Windows.Application.Current.Dispatcher.BeginInvoke(action)));
        builder.Services.AddSingleton<IMotionGate, MotionGate>();
        builder.Services.AddSingleton<IslandWindow>();
        builder.Services.AddSingleton<RestartService>();
        builder.Services.AddSingleton(sp => new ShortcutController(
            sp.GetRequiredService<IslandWindow>(),
            sp.GetRequiredService<IIslandStateMachine>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILogger<ShortcutController>>()));
        builder.Services.AddSingleton(sp => new WidgetPresenter(
            sp.GetRequiredService<WidgetRegistry>(),
            sp.GetRequiredService<IslandWindow>().NotchView,
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IIslandStateMachine>(),
            sp.GetRequiredService<LifecycleCoordinator>(),
            sp.GetRequiredService<ILogger<WidgetPresenter>>()));
        builder.Services.AddSingleton<AppInfo>();
        builder.Services.AddSingleton<IStartupRegistration>(_ => TestHooks.Get(TestHooks.RegistryRootVariable) is { Length: > 0 } testRoot
            ? new RegistryStartupRegistration(testRoot, checkMachinePolicy: false)
            : new RegistryStartupRegistration());
        builder.Services.AddSingleton(sp => new StartupCoordinator(
            sp.GetRequiredService<IStartupRegistration>(),
            sp.GetRequiredService<ISettingsService>(),
            System.IO.Path.Combine(AppContext.BaseDirectory, LauncherContract.LauncherExecutable),
            sp.GetRequiredService<ILogger<StartupCoordinator>>()));
        builder.Services.AddSingleton(sp => new SettingsWindowService(() =>
        {
            var info = sp.GetRequiredService<AppInfo>();
            var launch = sp.GetRequiredService<LaunchArguments>();
            var components = sp.GetRequiredService<ComponentHost>();
            var lifecycle = sp.GetRequiredService<LifecycleCoordinator>();
            return new SettingsWindow(
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<IMonitorService>(),
                sp.GetRequiredService<DiagnosticBundleBuilder>(),
                new SettingsWindowContext(
                    info.DisplayVersion,
                    info.Copyright,
                    root,
                    sp.GetRequiredService<FileLoggerOptions>().Directory,
                    () => new DiagnosticContext(info.Version, launch.SafeMode, info.SettingsOutcome?.ToString(), components.Statuses),
                    () => lifecycle.Reason is not null,
                    () => sp.GetRequiredService<StartupCoordinator>().State,
                    () => sp.GetRequiredService<ShortcutController>().State),
                sp.GetRequiredService<ILogger<SettingsWindow>>());
        }));
        builder.Services.AddSingleton(sp => new TrayController(
            sp.GetRequiredService<IIslandStateMachine>(),
            sp.GetRequiredService<LifecycleCoordinator>(),
            sp.GetRequiredService<LaunchArguments>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IMonitorService>(),
            () => sp.GetRequiredService<IslandWindow>().LastPlacement?.Monitor.Id,
            sp.GetRequiredService<RestartService>(),
            () => sp.GetRequiredService<SettingsWindowService>().Show(),
            sp.GetRequiredService<FileLoggerOptions>().Directory,
            sp.GetRequiredService<ILogger<TrayController>>()));
        builder.Services.AddSingleton<Win32FullscreenDetector>();
        builder.Services.AddSingleton<IFullscreenDetector>(sp => sp.GetRequiredService<Win32FullscreenDetector>());
        builder.Services.AddSingleton(sp => new FullscreenCoordinator(
            sp.GetRequiredService<Win32FullscreenDetector>(),
            sp.GetRequiredService<IIslandStateMachine>(),
            sp.GetRequiredService<ISettingsService>(),
            () => sp.GetRequiredService<IslandWindow>().LastPlacement?.Monitor.Id,
            sp.GetRequiredService<TimeProvider>(),
            System.Windows.Application.Current.Dispatcher,
            sp.GetRequiredService<ILogger<FullscreenCoordinator>>()));

        return builder;
    }
}
