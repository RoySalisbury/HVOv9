using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Abstractions;
using HVO.SkyMonitorV5.Data.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.HostedServices;

/// <summary>
/// Ensures the SkyMonitor telemetry database is created and migrations applied during application startup.
/// </summary>
public sealed class TelemetryStoreBootstrapper : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISkyMonitorDataPathProvider _pathProvider;
    private readonly ILogger<TelemetryStoreBootstrapper> _logger;

    public TelemetryStoreBootstrapper(
        IServiceScopeFactory scopeFactory,
        ISkyMonitorDataPathProvider pathProvider,
        ILogger<TelemetryStoreBootstrapper> logger)
    {
        _scopeFactory = scopeFactory;
        _pathProvider = pathProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var databasePath = _pathProvider.ResolvePath("telemetry/sm-telemetry.db");
        _logger.LogInformation("Ensuring telemetry store is present at {DatabasePath}.", databasePath);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SkyMonitorTelemetryContext>();

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Telemetry store migrations completed successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
