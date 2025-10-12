using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Telemetry.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.Data.Telemetry.Repositories;

public sealed class SkyMonitorTelemetryRepository : ISkyMonitorTelemetryRepository
{
    private readonly IDbContextFactory<SkyMonitorTelemetryContext> _contextFactory;
    private readonly ILogger<SkyMonitorTelemetryRepository> _logger;

    public SkyMonitorTelemetryRepository(
        IDbContextFactory<SkyMonitorTelemetryContext> contextFactory,
        ILogger<SkyMonitorTelemetryRepository> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task SaveRemoteDispatchAttemptAsync(RemoteDispatchAttemptEntity entity, CancellationToken cancellationToken = default)
        => SaveAsync(entity, context => context.RemoteDispatchAttempts, cancellationToken);

    public Task SaveBackgroundStackerSampleAsync(BackgroundStackerSampleEntity entity, CancellationToken cancellationToken = default)
        => SaveAsync(entity, context => context.BackgroundStackerSamples, cancellationToken);

    public Task SaveCapturePacingSampleAsync(CapturePacingSampleEntity entity, CancellationToken cancellationToken = default)
        => SaveAsync(entity, context => context.CapturePacingSamples, cancellationToken);

    public Task SaveProcessingQueueSampleAsync(ProcessingQueueSampleEntity entity, CancellationToken cancellationToken = default)
        => SaveAsync(entity, context => context.ProcessingQueueSamples, cancellationToken);

    public Task SaveFilterMetricSampleAsync(FilterMetricSampleEntity entity, CancellationToken cancellationToken = default)
        => SaveAsync(entity, context => context.FilterMetricSamples, cancellationToken);

    public Task SaveTelemetryEventAsync(TelemetryEventEntity entity, CancellationToken cancellationToken = default)
        => SaveAsync(entity, context => context.TelemetryEvents, cancellationToken);

    private async Task SaveAsync<TEntity>(
        TEntity entity,
        Func<SkyMonitorTelemetryContext, DbSet<TEntity>> setAccessor,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(setAccessor);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var set = setAccessor(context);
            await set.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist telemetry entity of type {EntityType}.", typeof(TEntity).Name);
            throw;
        }
    }
}
