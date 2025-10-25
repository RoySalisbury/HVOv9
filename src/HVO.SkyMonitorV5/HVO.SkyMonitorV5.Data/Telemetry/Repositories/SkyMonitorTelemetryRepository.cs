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

    public Task SaveFrameExportAttemptAsync(FrameExportAttemptEntity entity, CancellationToken cancellationToken = default)
        => SaveAsync(entity, context => context.FrameExportAttempts, cancellationToken);

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

    public async Task<TelemetrySystemProfileEntity> UpsertSystemProfileAsync(TelemetrySystemProfileEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await context.TelemetrySystemProfiles.SingleOrDefaultAsync(profile => profile.SystemHash == entity.SystemHash, cancellationToken).ConfigureAwait(false);

            if (existing is null)
            {
                await context.TelemetrySystemProfiles.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return entity;
            }

            UpdateString(existing, static profile => profile.MachineName, entity.MachineName, static (profile, value) => profile.MachineName = value);
            UpdateString(existing, static profile => profile.HostName, entity.HostName, static (profile, value) => profile.HostName = value);
            UpdateString(existing, static profile => profile.OperatingSystem, entity.OperatingSystem, static (profile, value) => profile.OperatingSystem = value);
            UpdateString(existing, static profile => profile.OsArchitecture, entity.OsArchitecture, static (profile, value) => profile.OsArchitecture = value);
            UpdateString(existing, static profile => profile.ProcessArchitecture, entity.ProcessArchitecture, static (profile, value) => profile.ProcessArchitecture = value);
            UpdateString(existing, static profile => profile.FrameworkDescription, entity.FrameworkDescription, static (profile, value) => profile.FrameworkDescription = value);
            UpdateString(existing, static profile => profile.CpuModel, entity.CpuModel, static (profile, value) => profile.CpuModel = value);
            UpdateString(existing, static profile => profile.HardwareModel, entity.HardwareModel, static (profile, value) => profile.HardwareModel = value);

            if (entity.ProcessorCount.HasValue)
            {
                existing.ProcessorCount = entity.ProcessorCount;
            }

            if (entity.TotalMemoryMegabytes.HasValue)
            {
                existing.TotalMemoryMegabytes = entity.TotalMemoryMegabytes;
            }

            if (entity.IsContainerized.HasValue)
            {
                existing.IsContainerized = entity.IsContainerized;
            }

            if (!string.IsNullOrWhiteSpace(entity.AdditionalPropertiesJson))
            {
                existing.AdditionalPropertiesJson = entity.AdditionalPropertiesJson;
            }

            if (entity.FirstSeenAtUtc < existing.FirstSeenAtUtc)
            {
                existing.FirstSeenAtUtc = entity.FirstSeenAtUtc;
            }

            existing.LastSeenAtUtc = entity.LastSeenAtUtc;

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert telemetry system profile with hash {SystemHash}.", entity.SystemHash);
            throw;
        }
    }

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

    private static void UpdateString(
        TelemetrySystemProfileEntity target,
        Func<TelemetrySystemProfileEntity, string?> getter,
        string? candidate,
        Action<TelemetrySystemProfileEntity, string> setter)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var normalized = candidate.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        var current = getter(target);
        if (!string.Equals(current, normalized, StringComparison.Ordinal))
        {
            setter(target, normalized);
        }
    }
}
