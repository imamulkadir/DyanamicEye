using DynamicIsland.App;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.IntegrationTests;

public sealed class AppHostTests
{
    [Fact]
    public async Task Host_builds_with_validation_and_starts_and_stops()
    {
        using var host = AppHost.CreateBuilder().Build();
        var cancellationToken = TestContext.Current.CancellationToken;

        await host.StartAsync(cancellationToken);
        await host.StopAsync(cancellationToken);
    }

    [Fact]
    public void Host_ignores_command_line_and_environment_configuration()
    {
        using var host = AppHost.CreateBuilder().Build();
        var configuration = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();

        Assert.DoesNotContain(configuration.Providers, p => p is CommandLineConfigurationProvider);
        Assert.DoesNotContain(configuration.Providers, p => p is EnvironmentVariablesConfigurationProvider);
    }

    [Fact]
    public void Host_registers_no_default_log_providers()
    {
        using var host = AppHost.CreateBuilder().Build();

        Assert.Empty(host.Services.GetServices<ILoggerProvider>());
    }
}
