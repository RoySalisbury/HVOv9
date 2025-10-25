using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Abstractions;
using HVO.SkyMonitorV5.Data.Telemetry;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Telemetry;
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
    private readonly IDataStoreBootstrapStatus _status;
    private readonly IObservatoryClock _clock;
    private readonly ILogger<TelemetryStoreBootstrapper> _logger;

    public TelemetryStoreBootstrapper(
        IServiceScopeFactory scopeFactory,
        ISkyMonitorDataPathProvider pathProvider,
        IDataStoreBootstrapStatus status,
        IObservatoryClock clock,
        ILogger<TelemetryStoreBootstrapper> logger)
    {
        _scopeFactory = scopeFactory;
        _pathProvider = pathProvider;
        _status = status;
        _clock = clock;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var databasePath = _pathProvider.ResolvePath("telemetry/sm-telemetry.db");
        var startedAtUtc = _clock.UtcNow;
        _logger.LogInformation("Ensuring telemetry store is present at {DatabasePath}.", databasePath);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var context = provider.GetRequiredService<SkyMonitorTelemetryContext>();
        try
        {
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            var registrar = provider.GetService<ITelemetrySystemProfileRegistrar>();
            if (registrar is not null)
            {
                await registrar.RegisterAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);
            }

            var completedAtUtc = _clock.UtcNow;
            _status.ReportTelemetrySuccess(databasePath, startedAtUtc, completedAtUtc);
            _logger.LogInformation("Telemetry store migrations completed successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _status.ReportTelemetryFailure(databasePath, startedAtUtc, ex);
            _logger.LogError(ex, "Failed to migrate telemetry store at {DatabasePath}.", databasePath);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
