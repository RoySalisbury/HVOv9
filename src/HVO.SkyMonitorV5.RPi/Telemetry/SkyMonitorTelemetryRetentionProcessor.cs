using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Telemetry;
using HVO.SkyMonitorV5.Data.Telemetry.Entities;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal sealed class SkyMonitorTelemetryRetentionProcessor
{
    private readonly IDbContextFactory<SkyMonitorTelemetryContext> _contextFactory;
    private readonly IObservatoryClock _clock;
    private readonly ILogger<SkyMonitorTelemetryRetentionProcessor> _logger;

    public SkyMonitorTelemetryRetentionProcessor(
        IDbContextFactory<SkyMonitorTelemetryContext> contextFactory,
        IObservatoryClock clock,
        ILogger<SkyMonitorTelemetryRetentionProcessor> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TelemetryRetentionSummary> RunAsync(SkyMonitorTelemetryRetentionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var nowUtc = _clock.UtcNow;
        var summaryBuilder = new TelemetryRetentionSummaryBuilder();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        summaryBuilder.RemoteDispatchPurged += await PurgeAsync(
            context,
            context.RemoteDispatchAttempts,
            options.RemoteDispatch,
            nowUtc,
            static entity => entity.AttemptedAtUtc,
            static entity => entity.Id,
            cancellationToken).ConfigureAwait(false);

        summaryBuilder.BackgroundStackerPurged += await PurgeAsync(
            context,
            context.BackgroundStackerSamples,
            options.BackgroundStacker,
            nowUtc,
            static entity => entity.CapturedAtUtc,
            static entity => entity.Id,
            cancellationToken).ConfigureAwait(false);

        summaryBuilder.CapturePacingPurged += await PurgeAsync(
            context,
            context.CapturePacingSamples,
            options.CapturePacing,
            nowUtc,
            static entity => entity.CapturedAtUtc,
            static entity => entity.Id,
            cancellationToken).ConfigureAwait(false);

        summaryBuilder.ProcessingQueuePurged += await PurgeAsync(
            context,
            context.ProcessingQueueSamples,
            options.ProcessingQueue,
            nowUtc,
            static entity => entity.CapturedAtUtc,
            static entity => entity.Id,
            cancellationToken).ConfigureAwait(false);

        summaryBuilder.FilterMetricsPurged += await PurgeAsync(
            context,
            context.FilterMetricSamples,
            options.FilterMetrics,
            nowUtc,
            static entity => entity.CapturedAtUtc,
            static entity => entity.Id,
            cancellationToken).ConfigureAwait(false);

        summaryBuilder.TelemetryEventsPurged += await PurgeAsync(
            context,
            context.TelemetryEvents,
            options.TelemetryEvents,
            nowUtc,
            static entity => entity.OccurredAtUtc,
            static entity => entity.Id,
            cancellationToken).ConfigureAwait(false);

        var summary = summaryBuilder.Build();

        if (summary.TotalPurged > 0 && options.VacuumAfterPurge)
        {
            summary = await VacuumAsync(context, summary, cancellationToken).ConfigureAwait(false);
        }

        if (summary.TotalPurged > 0)
        {
            _logger.LogInformation(
                "Telemetry retention sweep removed {Total} rows (remote: {Remote}, stacker: {Stacker}, pacing: {Pacing}, processing: {Processing}, filters: {Filters}, events: {Events}). Vacuum attempted: {VacuumAttempted}, succeeded: {VacuumSucceeded}.",
                summary.TotalPurged,
                summary.RemoteDispatchPurged,
                summary.BackgroundStackerPurged,
                summary.CapturePacingPurged,
                summary.ProcessingQueuePurged,
                summary.FilterMetricsPurged,
                summary.TelemetryEventsPurged,
                summary.VacuumAttempted,
                summary.VacuumSucceeded);
        }
        else
        {
            _logger.LogDebug("Telemetry retention sweep completed without purging records.");
        }

        return summary;
    }

    private static async Task<int> PurgeAsync<TEntity>(
        SkyMonitorTelemetryContext context,
        DbSet<TEntity> set,
        TelemetryRetentionPolicy policy,
        DateTimeOffset nowUtc,
        Expression<Func<TEntity, DateTimeOffset>> timestampSelector,
        Expression<Func<TEntity, long>> idSelector,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(timestampSelector);
        ArgumentNullException.ThrowIfNull(idSelector);

        var totalRemoved = 0;

        if (policy.MaxAge is { } maxAge && maxAge > TimeSpan.Zero)
        {
            var cutoff = nowUtc - maxAge;
            var timestampAccessor = timestampSelector.Compile();
            var idAccessor = idSelector.Compile();

            var staleIds = new List<long>();

            await foreach (var entity in set.AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                if (timestampAccessor(entity) < cutoff)
                {
                    staleIds.Add(idAccessor(entity));
                }
            }

            if (staleIds.Count > 0)
            {
                totalRemoved += await DeleteByIdsAsync(context, set, staleIds, cancellationToken).ConfigureAwait(false);
            }
        }

        if (policy.MaxRecords is { } maxRecords && maxRecords > 0)
        {
            var removed = await RemoveOverflowRowsAsync(context, set, timestampSelector, idSelector, maxRecords, cancellationToken).ConfigureAwait(false);
            totalRemoved += removed;
        }

        return totalRemoved;
    }

    private static async Task<int> RemoveOverflowRowsAsync<TEntity>(
        SkyMonitorTelemetryContext context,
        DbSet<TEntity> set,
        Expression<Func<TEntity, DateTimeOffset>> timestampSelector,
        Expression<Func<TEntity, long>> idSelector,
        int maxRecords,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var currentCount = await set.CountAsync(cancellationToken).ConfigureAwait(false);
        var overflow = currentCount - maxRecords;
        if (overflow <= 0)
        {
            return 0;
        }

        var staleIds = await set
            .OrderByDescending(timestampSelector)
            .Skip(maxRecords)
            .Select(idSelector)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (staleIds.Count == 0)
        {
            return 0;
        }

        return await DeleteByIdsAsync(context, set, staleIds, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TelemetryRetentionSummary> VacuumAsync(
        SkyMonitorTelemetryContext context,
        TelemetryRetentionSummary summary,
        CancellationToken cancellationToken)
    {
        var vacuumAttempted = false;
        var vacuumSucceeded = false;

        try
        {
            vacuumAttempted = true;
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            vacuumSucceeded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telemetry retention VACUUM operation failed.");
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        return summary with
        {
            VacuumAttempted = vacuumAttempted,
            VacuumSucceeded = vacuumSucceeded
        };
    }

    private static async Task<int> DeleteByIdsAsync<TEntity>(
        SkyMonitorTelemetryContext context,
        DbSet<TEntity> set,
        IReadOnlyCollection<long> ids,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        var idList = ids as IList<long> ?? ids.ToList();

        var entities = await set
            .Where(entity => idList.Contains(EF.Property<long>(entity, "Id")))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entities.Count == 0)
        {
            return 0;
        }

        context.RemoveRange(entities);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entities.Count;
    }

    private sealed class TelemetryRetentionSummaryBuilder
    {
        public int RemoteDispatchPurged { get; set; }
        public int BackgroundStackerPurged { get; set; }
        public int CapturePacingPurged { get; set; }
        public int ProcessingQueuePurged { get; set; }
        public int FilterMetricsPurged { get; set; }
        public int TelemetryEventsPurged { get; set; }

        public TelemetryRetentionSummary Build()
        {
            var total = RemoteDispatchPurged
                + BackgroundStackerPurged
                + CapturePacingPurged
                + ProcessingQueuePurged
                + FilterMetricsPurged
                + TelemetryEventsPurged;

            return new TelemetryRetentionSummary(
                RemoteDispatchPurged,
                BackgroundStackerPurged,
                CapturePacingPurged,
                ProcessingQueuePurged,
                FilterMetricsPurged,
                TelemetryEventsPurged,
                total,
                VacuumAttempted: false,
                VacuumSucceeded: false);
        }
    }
}

internal sealed record TelemetryRetentionSummary(
    int RemoteDispatchPurged,
    int BackgroundStackerPurged,
    int CapturePacingPurged,
    int ProcessingQueuePurged,
    int FilterMetricsPurged,
    int TelemetryEventsPurged,
    int TotalPurged,
    bool VacuumAttempted,
    bool VacuumSucceeded);
