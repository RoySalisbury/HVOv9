using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Abstractions;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.RPi.Infrastructure;
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
    private readonly IDataStoreBootstrapStatus _status;
    private readonly IObservatoryClock _clock;
    private readonly ILogger<ConfigurationStoreBootstrapper> _logger;

    public ConfigurationStoreBootstrapper(
        IServiceScopeFactory scopeFactory,
        ISkyMonitorDataPathProvider pathProvider,
        IDataStoreBootstrapStatus status,
        IObservatoryClock clock,
        ILogger<ConfigurationStoreBootstrapper> logger)
    {
        _scopeFactory = scopeFactory;
        _pathProvider = pathProvider;
        _status = status;
        _clock = clock;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var databasePath = _pathProvider.ResolvePath("configuration/sm-config.db");
        var startedAtUtc = _clock.UtcNow;
        _logger.LogInformation("Ensuring configuration store is present at {DatabasePath}.", databasePath);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SkyMonitorConfigurationContext>();
        try
        {
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            var completedAtUtc = _clock.UtcNow;
            _status.ReportConfigurationSuccess(databasePath, startedAtUtc, completedAtUtc);
            _logger.LogInformation("Configuration store migrations completed successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _status.ReportConfigurationFailure(databasePath, startedAtUtc, ex);
            _logger.LogError(ex, "Failed to migrate configuration store at {DatabasePath}.", databasePath);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
