using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.Data.Abstractions;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Telemetry;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Storage;
using HVO.SkyMonitorV5.RPi.Telemetry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Services;

internal sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly IFrameStateStore _frameStateStore;
    private readonly IFrameFilterPipeline _frameFilterPipeline;
    private readonly IDbContextFactory<SkyMonitorConfigurationContext> _configurationContextFactory;
    private readonly IDbContextFactory<SkyMonitorTelemetryContext> _telemetryContextFactory;
    private readonly ISkyMonitorDataPathProvider _dataPathProvider;
    private readonly SkyMonitorTelemetryMetrics _telemetryMetrics;
    private readonly IDataStoreBootstrapStatus _bootstrapStatus;
    private readonly ILogger<DiagnosticsService> _logger;
    private readonly IObservatoryClock _clock;
    private readonly object _systemMetricsLock = new();
    private CpuSample? _lastCpuSample;
    private DateTimeOffset? _lastProcessCpuSampleAtUtc;
    private TimeSpan _lastProcessCpuTotalProcessorTime;
    private const string ConfigurationDatabaseRelativePath = "configuration/sm-config.db";
    private const string TelemetryDatabaseRelativePath = "telemetry/sm-telemetry.db";

    public DiagnosticsService(
        IFrameStateStore frameStateStore,
        IFrameFilterPipeline frameFilterPipeline,
        IDbContextFactory<SkyMonitorConfigurationContext> configurationContextFactory,
        IDbContextFactory<SkyMonitorTelemetryContext> telemetryContextFactory,
        ISkyMonitorDataPathProvider dataPathProvider,
        SkyMonitorTelemetryMetrics telemetryMetrics,
        IDataStoreBootstrapStatus bootstrapStatus,
        ILogger<DiagnosticsService> logger,
        IObservatoryClock clock)
    {
        _frameStateStore = frameStateStore ?? throw new ArgumentNullException(nameof(frameStateStore));
        _frameFilterPipeline = frameFilterPipeline ?? throw new ArgumentNullException(nameof(frameFilterPipeline));
        _configurationContextFactory = configurationContextFactory ?? throw new ArgumentNullException(nameof(configurationContextFactory));
        _telemetryContextFactory = telemetryContextFactory ?? throw new ArgumentNullException(nameof(telemetryContextFactory));
        _dataPathProvider = dataPathProvider ?? throw new ArgumentNullException(nameof(dataPathProvider));
        _telemetryMetrics = telemetryMetrics ?? throw new ArgumentNullException(nameof(telemetryMetrics));
        _bootstrapStatus = bootstrapStatus ?? throw new ArgumentNullException(nameof(bootstrapStatus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<Result<BackgroundStackerMetricsResponse>> GetBackgroundStackerMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = _frameStateStore.BackgroundStackerStatus;
            var fallbackSeconds = CalculateSecondsSinceLastFrame(out var fallbackCompletedAt);
            if (status is null)
            {
                _logger.LogDebug("Background stacker metrics requested before telemetry was available.");
                var metrics = ApplyFallback(CreateEmptyStackerMetrics(), fallbackSeconds, fallbackCompletedAt);
                return Task.FromResult(Result<BackgroundStackerMetricsResponse>.Success(metrics));
            }

            var response = MapBackgroundStackerMetrics(status, fallbackSeconds, fallbackCompletedAt);
            return Task.FromResult(Result<BackgroundStackerMetricsResponse>.Success(response));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while gathering background stacker metrics snapshot.");
            return Task.FromResult<Result<BackgroundStackerMetricsResponse>>(ex);
        }
    }

    public Task<Result<FilterMetricsSnapshot>> GetFilterMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_frameFilterPipeline is FrameFilterPipeline pipeline)
            {
                var snapshot = pipeline.GetMetricsSnapshot();
                return Task.FromResult(Result<FilterMetricsSnapshot>.Success(snapshot));
            }

            _logger.LogWarning("Frame filter pipeline implementation did not expose telemetry snapshot; returning empty telemetry.");
            return Task.FromResult(Result<FilterMetricsSnapshot>.Success(new FilterMetricsSnapshot(Array.Empty<FilterMetrics>())));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while gathering filter metrics snapshot.");
            return Task.FromResult<Result<FilterMetricsSnapshot>>(ex);
        }
    }

    public Task<Result<BackgroundStackerHistoryResponse>> GetBackgroundStackerHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var samples = _frameStateStore.GetBackgroundStackerHistory();
            var history = new BackgroundStackerHistoryResponse(_clock.LocalNow, samples);

            return Task.FromResult(Result<BackgroundStackerHistoryResponse>.Success(history));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while gathering background stacker history snapshot.");
            return Task.FromResult<Result<BackgroundStackerHistoryResponse>>(ex);
        }
    }

    public Task<Result<RemoteDispatchMetricsSnapshot>> GetRemoteDispatchMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metrics = _frameStateStore.RemoteDispatchMetrics;
            if (metrics is null)
            {
                var generatedAt = _clock.LocalNow;
                return Task.FromResult(Result<RemoteDispatchMetricsSnapshot>.Success(CreateEmptyRemoteDispatchMetrics(generatedAt)));
            }

            return Task.FromResult(Result<RemoteDispatchMetricsSnapshot>.Success(metrics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while gathering remote dispatch metrics snapshot.");
            return Task.FromResult<Result<RemoteDispatchMetricsSnapshot>>(ex);
        }
    }

    public Task<Result<RemoteDispatchHistoryResponse>> GetRemoteDispatchHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var samples = _frameStateStore.GetRemoteDispatchHistory();
            var history = new RemoteDispatchHistoryResponse(_clock.LocalNow, samples);

            return Task.FromResult(Result<RemoteDispatchHistoryResponse>.Success(history));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while gathering remote dispatch history snapshot.");
            return Task.FromResult<Result<RemoteDispatchHistoryResponse>>(ex);
        }
    }

    public Task<Result<SystemDiagnosticsSnapshot>> GetSystemDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var process = Process.GetCurrentProcess();

            var generatedAtUtc = _clock.UtcNow;
            var generatedAtLocal = _clock.LocalNow;
            var currentCpuSample = CaptureCpuSample(generatedAtUtc);

            CpuSample? previousCpuSample;
            DateTimeOffset? previousProcessTimestamp;
            TimeSpan previousProcessCpuTime;

            lock (_systemMetricsLock)
            {
                previousCpuSample = _lastCpuSample;
                _lastCpuSample = currentCpuSample;

                previousProcessTimestamp = _lastProcessCpuSampleAtUtc;
                previousProcessCpuTime = _lastProcessCpuTotalProcessorTime;

                _lastProcessCpuSampleAtUtc = generatedAtUtc;
                _lastProcessCpuTotalProcessorTime = process.TotalProcessorTime;
            }

            IReadOnlyList<CoreCpuLoad> coreLoads = Array.Empty<CoreCpuLoad>();
            double? totalCpuPercent = null;

            if (previousCpuSample is not null && currentCpuSample is not null)
            {
                coreLoads = ComputeCpuLoads(previousCpuSample, currentCpuSample, out totalCpuPercent);
            }

            var processCpuPercent = ComputeProcessCpuPercent(process, generatedAtUtc, previousProcessTimestamp, previousProcessCpuTime);
            var memorySnapshot = CaptureMemoryMetrics();

            var snapshot = new SystemDiagnosticsSnapshot(
                TotalCpuPercent: totalCpuPercent,
                ProcessCpuPercent: processCpuPercent,
                CoreCpuLoads: coreLoads,
                Memory: memorySnapshot,
                ProcessWorkingSetMegabytes: ToMegabytes(process.WorkingSet64),
                ProcessPrivateMegabytes: ToMegabytes(process.PrivateMemorySize64),
                ManagedMemoryMegabytes: ToMegabytes(GC.GetTotalMemory(forceFullCollection: false)),
                ThreadCount: process.Threads.Count,
                UptimeSeconds: Math.Max((generatedAtUtc - process.StartTime.ToUniversalTime()).TotalSeconds, 0d),
                GeneratedAtUtc: generatedAtUtc,
                GeneratedAtLocal: generatedAtLocal);

            return Task.FromResult(Result<SystemDiagnosticsSnapshot>.Success(snapshot));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while gathering system diagnostics snapshot.");
            return Task.FromResult(Result<SystemDiagnosticsSnapshot>.Failure(ex));
        }
    }

    public async Task<Result<DataStoreMetricsSnapshot>> GetDataStoreMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var generatedAtUtc = _clock.UtcNow;
            var generatedAtLocal = _clock.LocalNow;

            var bootstrapSnapshot = _bootstrapStatus.GetSnapshot();
            var telemetrySnapshot = _telemetryMetrics.GetTelemetrySnapshot();
            var retentionSnapshot = _telemetryMetrics.GetRetentionSnapshot();

            var configurationStore = await CreateConfigurationStoreMetricsAsync(bootstrapSnapshot.Configuration, cancellationToken).ConfigureAwait(false);
            var telemetryStore = await CreateTelemetryStoreMetricsAsync(bootstrapSnapshot.Telemetry, telemetrySnapshot, retentionSnapshot, cancellationToken).ConfigureAwait(false);

            var snapshot = new DataStoreMetricsSnapshot(
                GeneratedAtUtc: generatedAtUtc,
                GeneratedAtLocal: generatedAtLocal,
                ConfigurationStore: configurationStore,
                TelemetryStore: telemetryStore);

            return Result<DataStoreMetricsSnapshot>.Success(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while gathering data store metrics snapshot.");
            return Result<DataStoreMetricsSnapshot>.Failure(ex);
        }
    }

    private static BackgroundStackerMetricsResponse CreateEmptyStackerMetrics() => new(
        Enabled: false,
        QueueDepth: 0,
        QueueCapacity: 0,
        QueueFillPercentage: 0,
        PeakQueueDepth: 0,
        PeakQueueFillPercentage: 0,
        ProcessedFrameCount: 0,
        DroppedFrameCount: 0,
        QueuePressureLevel: 0,
        LastQueueLatencyMilliseconds: null,
        AverageQueueLatencyMilliseconds: null,
        MaxQueueLatencyMilliseconds: null,
        LastStackMilliseconds: null,
        AverageStackMilliseconds: null,
        LastFilterMilliseconds: null,
        AverageFilterMilliseconds: null,
        QueueMemoryBytes: 0,
        PeakQueueMemoryBytes: 0,
        QueueMemoryMegabytes: 0,
        PeakQueueMemoryMegabytes: 0,
        LastEnqueuedAt: null,
        LastCompletedAt: null,
        SecondsSinceLastCompleted: null,
        LastFrameNumber: null);

    private async Task<DataStoreInstanceMetrics> CreateConfigurationStoreMetricsAsync(DataStoreBootstrapState bootstrapState, CancellationToken cancellationToken)
    {
        var databasePath = ResolveDatabasePath(ConfigurationDatabaseRelativePath, bootstrapState.DatabasePath);
        var fileStats = await CaptureFileStatsAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var tables = fileStats.Exists
            ? await CaptureConfigurationTableMetricsAsync(cancellationToken).ConfigureAwait(false)
            : Array.Empty<DataStoreTableMetric>();

        return new DataStoreInstanceMetrics(
            DatabasePath: databasePath,
            Exists: fileStats.Exists,
            FileBytes: fileStats.LengthBytes,
            FileMegabytes: fileStats.Megabytes,
            PageCount: fileStats.PageCount,
            PageSizeBytes: fileStats.PageSizeBytes,
            FreePages: fileStats.FreePages,
            Tables: tables,
            Bootstrap: MapBootstrapState(bootstrapState),
            TelemetryIngestion: null,
            TelemetryRetention: null);
    }

    private async Task<DataStoreInstanceMetrics> CreateTelemetryStoreMetricsAsync(
        DataStoreBootstrapState bootstrapState,
        TelemetryMetricsSnapshot telemetrySnapshot,
        TelemetryRetentionSnapshot retentionSnapshot,
        CancellationToken cancellationToken)
    {
        var databasePath = ResolveDatabasePath(TelemetryDatabaseRelativePath, bootstrapState.DatabasePath);
        var fileStats = await CaptureFileStatsAsync(databasePath, cancellationToken).ConfigureAwait(false);
        var tables = fileStats.Exists
            ? await CaptureTelemetryTableMetricsAsync(cancellationToken).ConfigureAwait(false)
            : Array.Empty<DataStoreTableMetric>();

        return new DataStoreInstanceMetrics(
            DatabasePath: databasePath,
            Exists: fileStats.Exists,
            FileBytes: fileStats.LengthBytes,
            FileMegabytes: fileStats.Megabytes,
            PageCount: fileStats.PageCount,
            PageSizeBytes: fileStats.PageSizeBytes,
            FreePages: fileStats.FreePages,
            Tables: tables,
            Bootstrap: MapBootstrapState(bootstrapState),
            TelemetryIngestion: MapTelemetryMetrics(telemetrySnapshot),
            TelemetryRetention: MapRetentionMetrics(retentionSnapshot));
    }

    private string ResolveDatabasePath(string relativePath, string bootstrapReportedPath)
    {
        if (!string.IsNullOrWhiteSpace(bootstrapReportedPath) && Path.IsPathRooted(bootstrapReportedPath))
        {
            return bootstrapReportedPath;
        }

        return _dataPathProvider.ResolvePath(relativePath);
    }

    private async Task<IReadOnlyList<DataStoreTableMetric>> CaptureConfigurationTableMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _configurationContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var tables = new List<DataStoreTableMetric>
            {
                new("observatory_site", await context.ObservatorySites.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("camera_catalog_camera", await context.CameraCatalogCameras.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("camera_catalog_lens", await context.CameraCatalogLenses.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("rig_catalog_entry", await context.RigCatalogEntries.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("camera_adapter_config", await context.CameraAdapters.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("camera_pipeline_config", await context.CameraPipelineConfigurations.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("star_catalog_settings", await context.StarCatalogSettings.LongCountAsync(cancellationToken).ConfigureAwait(false))
            };

            return tables;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to gather configuration store table metrics.");
            return Array.Empty<DataStoreTableMetric>();
        }
    }

    private async Task<IReadOnlyList<DataStoreTableMetric>> CaptureTelemetryTableMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _telemetryContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var tables = new List<DataStoreTableMetric>
            {
                new("remote_dispatch_attempt", await context.RemoteDispatchAttempts.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("background_stacker_sample", await context.BackgroundStackerSamples.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("capture_pacing_sample", await context.CapturePacingSamples.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("processing_queue_sample", await context.ProcessingQueueSamples.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("filter_metric_sample", await context.FilterMetricSamples.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("telemetry_event", await context.TelemetryEvents.LongCountAsync(cancellationToken).ConfigureAwait(false)),
                new("telemetry_system_profile", await context.TelemetrySystemProfiles.LongCountAsync(cancellationToken).ConfigureAwait(false))
            };

            return tables;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to gather telemetry store table metrics.");
            return Array.Empty<DataStoreTableMetric>();
        }
    }

    private async Task<DataStoreFileStats> CaptureFileStatsAsync(string databasePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(databasePath);
        if (!fileInfo.Exists)
        {
            return DataStoreFileStats.Missing;
        }

        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());

            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var pageCount = await ExecutePragmaLongAsync(connection, "PRAGMA page_count;", cancellationToken).ConfigureAwait(false);
            var pageSize = await ExecutePragmaLongAsync(connection, "PRAGMA page_size;", cancellationToken).ConfigureAwait(false);
            var freePages = await ExecutePragmaLongAsync(connection, "PRAGMA freelist_count;", cancellationToken).ConfigureAwait(false);

            return new DataStoreFileStats(
                Exists: true,
                LengthBytes: fileInfo.Length,
                Megabytes: ToMegabytes(fileInfo.Length),
                PageCount: pageCount,
                PageSizeBytes: pageSize,
                FreePages: freePages);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture SQLite statistics for {DatabasePath}.", databasePath);
            return new DataStoreFileStats(
                Exists: true,
                LengthBytes: fileInfo.Length,
                Megabytes: ToMegabytes(fileInfo.Length),
                PageCount: null,
                PageSizeBytes: null,
                FreePages: null);
        }
    }

    private static async Task<long?> ExecutePragmaLongAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return ConvertToNullableLong(result);
    }

    private static long? ConvertToNullableLong(object? value)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        if (value is long longValue)
        {
            return longValue;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DataStoreBootstrapStatusMetrics MapBootstrapState(DataStoreBootstrapState state)
    {
        return new DataStoreBootstrapStatusMetrics(
            Ran: state.Ran,
            Succeeded: state.Succeeded,
            StartedAtUtc: state.StartedAtUtc,
            CompletedAtUtc: state.CompletedAtUtc,
            ErrorMessage: state.ErrorMessage);
    }

    private static TelemetryIngestionMetricsSummary MapTelemetryMetrics(TelemetryMetricsSnapshot snapshot)
    {
        return new TelemetryIngestionMetricsSummary(
            QueueDepth: snapshot.QueueDepth,
            LastIngestionLatencyMilliseconds: snapshot.LastIngestionLatencyMilliseconds,
            LastRetentionDurationMilliseconds: snapshot.LastRetentionDurationMilliseconds);
    }

    private static TelemetryRetentionSummaryMetrics MapRetentionMetrics(TelemetryRetentionSnapshot snapshot)
    {
        return new TelemetryRetentionSummaryMetrics(
            LastStartedAtUtc: snapshot.LastStartedAtUtc,
            LastCompletedAtUtc: snapshot.LastCompletedAtUtc,
            LastDuration: snapshot.LastDuration,
            RemoteDispatchPurged: snapshot.RemoteDispatchPurged,
            BackgroundStackerPurged: snapshot.BackgroundStackerPurged,
            CapturePacingPurged: snapshot.CapturePacingPurged,
            ProcessingQueuePurged: snapshot.ProcessingQueuePurged,
            FilterMetricsPurged: snapshot.FilterMetricsPurged,
            TelemetryEventsPurged: snapshot.TelemetryEventsPurged,
            TotalPurged: snapshot.TotalPurged,
            VacuumAttempted: snapshot.VacuumAttempted,
            VacuumSucceeded: snapshot.VacuumSucceeded);
    }

    private sealed record DataStoreFileStats(
        bool Exists,
        long? LengthBytes,
        double? Megabytes,
        long? PageCount,
        long? PageSizeBytes,
        long? FreePages)
    {
        public static DataStoreFileStats Missing { get; } = new(false, null, null, null, null, null);
    }

    private static RemoteDispatchMetricsSnapshot CreateEmptyRemoteDispatchMetrics(DateTimeOffset generatedAt) => new(
        GeneratedAt: generatedAt,
        SampleCount: 0,
        SuccessCount: 0,
        FailureCount: 0,
        SkippedCount: 0,
        SuccessRatePercent: 0,
        AverageLatencyMilliseconds: null,
        PeakLatencyMilliseconds: null,
        LastLatencyMilliseconds: null,
        LastPayloadBytes: null,
        LastPayloadContentType: null,
        LastPayloadExtension: null,
        FormatCounts: Array.Empty<RemoteDispatchFormatSummary>());

    private BackgroundStackerMetricsResponse MapBackgroundStackerMetrics(
        BackgroundStackerStatus status,
        double? fallbackSecondsSinceLastFrame,
        DateTimeOffset? fallbackCompletedAt)
    {
        DateTimeOffset? lastEnqueuedAt = status.LastEnqueuedAt is { } enqueued
            ? _clock.ToLocal(enqueued)
            : null;
        DateTimeOffset? lastCompletedAt = status.LastCompletedAt is { } completed
            ? _clock.ToLocal(completed)
            : null;

        var computedSeconds = lastCompletedAt is { } completedAt
            ? CalculateElapsedSeconds(completedAt)
            : null;

        var secondsSinceLastCompleted = ResolveSeconds(
            computedSeconds,
            status.SecondsSinceLastCompleted,
            fallbackSecondsSinceLastFrame);

        var effectiveLastCompletedAt = lastCompletedAt ?? fallbackCompletedAt;

        return new BackgroundStackerMetricsResponse(
            Enabled: status.Enabled,
            QueueDepth: status.QueueDepth,
            QueueCapacity: status.QueueCapacity,
            QueueFillPercentage: status.QueueFillPercentage,
            PeakQueueDepth: status.PeakQueueDepth,
            PeakQueueFillPercentage: status.PeakQueueFillPercentage,
            ProcessedFrameCount: status.ProcessedFrameCount,
            DroppedFrameCount: status.DroppedFrameCount,
            QueuePressureLevel: status.QueuePressureLevel,
            LastQueueLatencyMilliseconds: status.LastQueueLatencyMilliseconds,
            AverageQueueLatencyMilliseconds: status.AverageQueueLatencyMilliseconds,
            MaxQueueLatencyMilliseconds: status.MaxQueueLatencyMilliseconds,
            LastStackMilliseconds: status.LastStackMilliseconds,
            AverageStackMilliseconds: status.AverageStackMilliseconds,
            LastFilterMilliseconds: status.LastFilterMilliseconds,
            AverageFilterMilliseconds: status.AverageFilterMilliseconds,
            QueueMemoryBytes: status.QueueMemoryBytes,
            PeakQueueMemoryBytes: status.PeakQueueMemoryBytes,
            QueueMemoryMegabytes: status.QueueMemoryMegabytes,
            PeakQueueMemoryMegabytes: status.PeakQueueMemoryMegabytes,
            LastEnqueuedAt: lastEnqueuedAt,
            LastCompletedAt: effectiveLastCompletedAt,
            SecondsSinceLastCompleted: secondsSinceLastCompleted,
            LastFrameNumber: status.LastFrameNumber);
    }

    private double? CalculateElapsedSeconds(DateTimeOffset completedAtLocal)
    {
        var elapsed = (_clock.LocalNow - completedAtLocal).TotalSeconds;
        return SanitizeSeconds(elapsed);
    }

    private BackgroundStackerMetricsResponse ApplyFallback(
        BackgroundStackerMetricsResponse metrics,
        double? fallbackSeconds,
        DateTimeOffset? fallbackCompletedAt)
    {
        var sanitizedSeconds = SanitizeSeconds(fallbackSeconds);
        if (!sanitizedSeconds.HasValue)
        {
            return metrics;
        }

        return metrics with
        {
            SecondsSinceLastCompleted = sanitizedSeconds,
            LastCompletedAt = metrics.LastCompletedAt ?? fallbackCompletedAt
        };
    }

    private static double? ResolveSeconds(double? computedSeconds, double? reportedSeconds, double? fallbackSeconds)
    {
        return SanitizeSeconds(computedSeconds)
            ?? SanitizeSeconds(reportedSeconds)
            ?? SanitizeSeconds(fallbackSeconds);
    }

    private static double? SanitizeSeconds(double? seconds)
    {
        if (!seconds.HasValue)
        {
            return null;
        }

        var value = seconds.Value;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return null;
        }

        if (value < 0)
        {
            value = 0d;
        }

        return value;
    }

    private double? CalculateSecondsSinceLastFrame(out DateTimeOffset? lastFrameTimestamp)
    {
        lastFrameTimestamp = _frameStateStore.LastFrameTimestamp;
        if (lastFrameTimestamp is not { } timestamp)
        {
            return null;
        }

        var elapsed = (_clock.LocalNow - timestamp).TotalSeconds;
        return SanitizeSeconds(elapsed);
    }

    private sealed record CpuSample(DateTimeOffset Timestamp, IReadOnlyDictionary<string, CpuCounters> Counters);

    private readonly record struct CpuCounters(long Idle, long Total);

    private static CpuSample? CaptureCpuSample(DateTimeOffset timestamp)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            using var reader = new StreamReader("/proc/stat");
            var counters = new Dictionary<string, CpuCounters>(StringComparer.OrdinalIgnoreCase);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (!line.StartsWith("cpu", StringComparison.Ordinal))
                {
                    break;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                {
                    continue;
                }

                var values = new long[Math.Min(parts.Length - 1, 8)];
                var valid = true;

                for (var i = 0; i < values.Length; i++)
                {
                    if (!long.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                    {
                        valid = false;
                        break;
                    }
                }

                if (!valid)
                {
                    continue;
                }

                long total = 0;
                for (var i = 0; i < values.Length; i++)
                {
                    total += values[i];
                }

                if (total <= 0)
                {
                    continue;
                }

                var idle = values.Length > 3 ? values[3] : 0L;
                if (values.Length > 4)
                {
                    idle += values[4];
                }

                counters[parts[0]] = new CpuCounters(idle, total);
            }

            return counters.Count == 0 ? null : new CpuSample(timestamp, counters);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<CoreCpuLoad> ComputeCpuLoads(CpuSample previous, CpuSample current, out double? totalPercent)
    {
        totalPercent = null;
        var loads = new List<CoreCpuLoad>();

        foreach (var (name, counters) in current.Counters)
        {
            if (!previous.Counters.TryGetValue(name, out var prior))
            {
                continue;
            }

            var deltaTotal = counters.Total - prior.Total;
            var deltaIdle = counters.Idle - prior.Idle;

            if (deltaTotal <= 0)
            {
                continue;
            }

            var usage = ClampPercent((double)(deltaTotal - deltaIdle) / deltaTotal * 100d);

            if (string.Equals(name, "cpu", StringComparison.OrdinalIgnoreCase))
            {
                totalPercent = usage;
                continue;
            }

            if (!name.StartsWith("cpu", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            loads.Add(new CoreCpuLoad(name, usage));
        }

        loads.Sort(static (a, b) =>
        {
            var aHasIndex = TryParseCpuIndex(a.Name, out var aIndex);
            var bHasIndex = TryParseCpuIndex(b.Name, out var bIndex);

            if (aHasIndex && bHasIndex)
            {
                return aIndex.CompareTo(bIndex);
            }

            if (aHasIndex)
            {
                return -1;
            }

            if (bHasIndex)
            {
                return 1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });

        return loads;
    }

    private static bool TryParseCpuIndex(string name, out int index)
    {
        if (name.Length > 3 && name.StartsWith("cpu", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            return true;
        }

        index = -1;
        return false;
    }

    private static double ComputeProcessCpuPercent(Process process, DateTimeOffset sampleTimestamp, DateTimeOffset? previousTimestamp, TimeSpan previousCpuTime)
    {
        var cpuCount = Math.Max(Environment.ProcessorCount, 1);
        var currentCpuTime = process.TotalProcessorTime;

        if (previousTimestamp is not null && previousTimestamp.Value < sampleTimestamp)
        {
            var wallSeconds = (sampleTimestamp - previousTimestamp.Value).TotalSeconds;
            var cpuSeconds = (currentCpuTime - previousCpuTime).TotalSeconds;

            if (wallSeconds > 0 && cpuSeconds >= 0)
            {
                return ClampPercent(cpuSeconds / (wallSeconds * cpuCount) * 100d);
            }
        }

        var uptimeSeconds = Math.Max((sampleTimestamp - process.StartTime.ToUniversalTime()).TotalSeconds, 0d);
        if (uptimeSeconds <= 0)
        {
            return 0d;
        }

        return ClampPercent(currentCpuTime.TotalSeconds / (uptimeSeconds * cpuCount) * 100d);
    }

    private static MemoryUsageSnapshot CaptureMemoryMetrics()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                using var reader = new StreamReader("/proc/meminfo");
                var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    var separatorIndex = line.IndexOf(':');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    var key = line[..separatorIndex];
                    var remainder = line[(separatorIndex + 1)..].Trim();
                    var tokens = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length == 0)
                    {
                        continue;
                    }

                    if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var valueKb))
                    {
                        continue;
                    }

                    values[key] = valueKb / 1024d;
                }

                double? GetValue(string key) => values.TryGetValue(key, out var parsed) ? parsed : null;

                var totalMb = GetValue("MemTotal");
                var freeMb = GetValue("MemFree");
                var availableMb = GetValue("MemAvailable");
                var cachedValue = GetValue("Cached");
                var reclaimableValue = GetValue("SReclaimable");
                double? cachedMb = cachedValue.HasValue || reclaimableValue.HasValue
                    ? (cachedValue ?? 0d) + (reclaimableValue ?? 0d)
                    : null;
                var buffersMb = GetValue("Buffers");

                double? usedMb = null;
                if (totalMb.HasValue)
                {
                    if (availableMb.HasValue)
                    {
                        usedMb = Math.Max(totalMb.Value - availableMb.Value, 0d);
                    }
                    else if (freeMb.HasValue)
                    {
                        usedMb = Math.Max(totalMb.Value - freeMb.Value, 0d);
                    }
                }

                var usagePercent = usedMb.HasValue && totalMb.HasValue && totalMb.Value > 0
                    ? (double?)ClampPercent(usedMb.Value / totalMb.Value * 100d)
                    : null;

                return new MemoryUsageSnapshot(
                    TotalMegabytes: totalMb,
                    UsedMegabytes: usedMb,
                    FreeMegabytes: freeMb,
                    AvailableMegabytes: availableMb,
                    CachedMegabytes: cachedMb,
                    BuffersMegabytes: buffersMb,
                    UsagePercent: usagePercent);
            }
            catch (IOException)
            {
                return new MemoryUsageSnapshot(null, null, null, null, null, null, null);
            }
            catch (UnauthorizedAccessException)
            {
                return new MemoryUsageSnapshot(null, null, null, null, null, null, null);
            }
        }

        try
        {
            var gcInfo = GC.GetGCMemoryInfo();

            double? totalMb = gcInfo.TotalAvailableMemoryBytes > 0
                ? gcInfo.TotalAvailableMemoryBytes / 1024d / 1024d
                : (gcInfo.HighMemoryLoadThresholdBytes > 0
                    ? gcInfo.HighMemoryLoadThresholdBytes / 1024d / 1024d
                    : null);

            double? usedMb = gcInfo.MemoryLoadBytes > 0
                ? gcInfo.MemoryLoadBytes / 1024d / 1024d
                : null;

            double? availableMb = null;
            if (totalMb.HasValue && usedMb.HasValue)
            {
                availableMb = Math.Max(totalMb.Value - usedMb.Value, 0d);
            }

            var usagePercent = usedMb.HasValue && totalMb.HasValue && totalMb.Value > 0
                ? (double?)ClampPercent(usedMb.Value / totalMb.Value * 100d)
                : null;

            return new MemoryUsageSnapshot(
                TotalMegabytes: totalMb,
                UsedMegabytes: usedMb,
                FreeMegabytes: null,
                AvailableMegabytes: availableMb,
                CachedMegabytes: null,
                BuffersMegabytes: null,
                UsagePercent: usagePercent);
        }
        catch (PlatformNotSupportedException)
        {
            return new MemoryUsageSnapshot(null, null, null, null, null, null, null);
        }
        catch (InvalidOperationException)
        {
            return new MemoryUsageSnapshot(null, null, null, null, null, null, null);
        }
        catch (Exception)
        {
            return new MemoryUsageSnapshot(null, null, null, null, null, null, null);
        }
    }

    private static double ClampPercent(double value) => double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : 0d;

    private static double ToMegabytes(long bytes) => bytes / 1024d / 1024d;
}
