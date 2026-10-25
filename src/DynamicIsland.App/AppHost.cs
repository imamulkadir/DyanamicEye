using DynamicIsland.Core.Settings;
using DynamicIsland.Infrastructure;
using DynamicIsland.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DynamicIsland.App;

/// <summary>
/// Composition root. Builds the Generic Host without default sources: no command-line or environment
/// configuration (launch arguments are untrusted input) and no default log providers (local file sink comes in P01).
/// </summary>
public static class AppHost
{
    public static HostApplicationBuilder CreateBuilder()
    {
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

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(_ => new SettingsStoreOptions(AppDataPaths.Data));
        builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();

        builder.Services.AddSingleton<MainWindow>();

        return builder;
    }
}
