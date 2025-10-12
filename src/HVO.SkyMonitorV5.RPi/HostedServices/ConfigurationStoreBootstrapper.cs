using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Abstractions;
using HVO.SkyMonitorV5.Data.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.HostedServices;

/// <summary>
/// Ensures the SkyMonitor configuration database exists and migrations are applied before the runtime uses it.
/// </summary>
public sealed class ConfigurationStoreBootstrapper : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISkyMonitorDataPathProvider _pathProvider;
    private readonly ILogger<ConfigurationStoreBootstrapper> _logger;

    public ConfigurationStoreBootstrapper(
        IServiceScopeFactory scopeFactory,
        ISkyMonitorDataPathProvider pathProvider,
        ILogger<ConfigurationStoreBootstrapper> logger)
    {
        _scopeFactory = scopeFactory;
        _pathProvider = pathProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var databasePath = _pathProvider.ResolvePath("configuration/sm-config.db");
        _logger.LogInformation("Ensuring configuration store is present at {DatabasePath}.", databasePath);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SkyMonitorConfigurationContext>();

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Configuration store migrations completed successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
