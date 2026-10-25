using System.IO;
using DynamicIsland.App;
using DynamicIsland.Core.Settings;
using DynamicIsland.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.IntegrationTests;

public sealed class AppHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DynamicIsland.IntegrationTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Host_builds_with_validation_and_starts_and_stops()
    {
        using var host = AppHost.CreateBuilder(_root).Build();
        var cancellationToken = TestContext.Current.CancellationToken;

        await host.StartAsync(cancellationToken);
        await host.StopAsync(cancellationToken);
    }

    [Fact]
    public void Host_ignores_command_line_and_environment_configuration()
    {
        using var host = AppHost.CreateBuilder(_root).Build();
        var configuration = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();

        Assert.DoesNotContain(configuration.Providers, p => p is CommandLineConfigurationProvider);
        Assert.DoesNotContain(configuration.Providers, p => p is EnvironmentVariablesConfigurationProvider);
    }

    [Fact]
    public void Host_uses_only_the_local_file_log_provider()
    {
        using var host = AppHost.CreateBuilder(_root).Build();

        var provider = Assert.Single(host.Services.GetServices<ILoggerProvider>());
        Assert.IsType<FileLoggerProvider>(provider);
    }

    [Fact]
    public void Logs_are_written_under_the_data_root_and_redacted()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using (var host = AppHost.CreateBuilder(_root).Build())
        {
            var logger = host.Services.GetRequiredService<ILogger<AppHostTests>>();
            logger.LogInformation("Probe {Path}", Path.Combine(profile, "secret-folder"));
            logger.LogDebug("Debug entries are below the minimum level");
        }

        var file = Assert.Single(Directory.GetFiles(Path.Combine(_root, "logs"), "dynamicisland-*.log"));
        var line = Assert.Single(File.ReadAllLines(file));
        Assert.Contains("%USERPROFILE%", line, StringComparison.Ordinal);
        Assert.DoesNotContain(profile.Replace(@"\", @"\\", StringComparison.Ordinal), line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Settings_store_uses_the_data_root()
    {
        using var host = AppHost.CreateBuilder(_root).Build();
        var store = host.Services.GetRequiredService<ISettingsStore>();

        await store.SaveAsync(new AppSettings(), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(_root, "data", "settings.json")));
    }
}
