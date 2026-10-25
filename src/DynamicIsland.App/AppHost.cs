using DynamicIsland.Core.Components;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Lifecycle;
using DynamicIsland.Core.Notifications;
using DynamicIsland.Core.Settings;
using DynamicIsland.Infrastructure;
using DynamicIsland.Infrastructure.Diagnostics;
using DynamicIsland.Infrastructure.Lifecycle;
using DynamicIsland.Infrastructure.Logging;
using DynamicIsland.Infrastructure.Settings;
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
        builder.Services.AddSingleton(sp => new DiagnosticBundleBuilder(
            System.IO.Path.Combine(root, AppDataPaths.LogsFolderName),
            dataDirectory,
            sp.GetRequiredService<LogRedactor>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(_ => new SettingsStoreOptions(dataDirectory));
        builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();

        builder.Services.AddSingleton<MainWindow>();

        return builder;
    }
}
