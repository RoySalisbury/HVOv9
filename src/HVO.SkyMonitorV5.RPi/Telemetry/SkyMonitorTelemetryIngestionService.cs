using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Telemetry.Entities;
using HVO.SkyMonitorV5.Data.Telemetry.Repositories;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal sealed class SkyMonitorTelemetryIngestionService : BackgroundService
{
    private readonly ISkyMonitorTelemetryIngestionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IObservatoryClock _clock;
    private readonly SkyMonitorTelemetryMetrics _metrics;
    private readonly ILogger<SkyMonitorTelemetryIngestionService> _logger;

    public SkyMonitorTelemetryIngestionService(
        ISkyMonitorTelemetryIngestionQueue queue,
        IServiceScopeFactory scopeFactory,
        IObservatoryClock clock,
        SkyMonitorTelemetryMetrics metrics,
        ILogger<SkyMonitorTelemetryIngestionService> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                var latency = _clock.UtcNow - workItem.EnqueuedAtUtc;
                if (latency < TimeSpan.Zero)
                {
                    latency = TimeSpan.Zero;
                }

                _metrics.ReportIngestionLatency(latency);

                await HandleWorkItemAsync(workItem, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telemetry ingestion failed for work item {WorkItemType}.", workItem.GetType().Name);
            }
        }
    }

    private async Task HandleWorkItemAsync(TelemetryWorkItem workItem, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISkyMonitorTelemetryRepository>();

        switch (workItem)
        {
            case TelemetryWorkItem.RemoteDispatchAttempt(_, var payload):
                await HandleRemoteDispatchAttemptAsync(repository, payload, cancellationToken).ConfigureAwait(false);
                break;
            case TelemetryWorkItem.FrameExportAttempt(_, var payload):
                await HandleFrameExportAttemptAsync(repository, payload, cancellationToken).ConfigureAwait(false);
                break;
            case TelemetryWorkItem.BackgroundStackerSample(_, var payload):
                await HandleBackgroundStackerSampleAsync(repository, payload, cancellationToken).ConfigureAwait(false);
                break;
            case TelemetryWorkItem.CapturePacingSample(_, var payload):
                await HandleCapturePacingSampleAsync(repository, payload, cancellationToken).ConfigureAwait(false);
                break;
            case TelemetryWorkItem.ProcessingQueueSample(_, var payload):
                await HandleProcessingQueueSampleAsync(repository, payload, cancellationToken).ConfigureAwait(false);
                break;
            case TelemetryWorkItem.FilterMetricSample(_, var payload):
                await HandleFilterMetricSampleAsync(repository, payload, cancellationToken).ConfigureAwait(false);
                break;
            case TelemetryWorkItem.TelemetryEvent(_, var payload):
                await HandleTelemetryEventAsync(repository, payload, cancellationToken).ConfigureAwait(false);
                break;
            default:
                _logger.LogWarning("Received unknown telemetry work item type {WorkItemType}.", workItem.GetType().Name);
                break;
        }
    }

    private static Task HandleRemoteDispatchAttemptAsync(ISkyMonitorTelemetryRepository repository, RemoteDispatchAttemptPayload payload, CancellationToken cancellationToken)
    {
        var entity = new RemoteDispatchAttemptEntity
        {
            AttemptedAtUtc = payload.AttemptedAtUtc,
            AttemptedAtLocal = payload.AttemptedAtLocal,
            Mode = Truncate(payload.Mode, 64)!,
            Outcome = (int)payload.Outcome,
            LatencyMilliseconds = payload.LatencyMilliseconds,
            PayloadBytes = payload.PayloadBytes,
            PayloadContentType = Truncate(payload.PayloadContentType, 128),
            PayloadExtension = Truncate(payload.PayloadExtension, 16),
            Message = Truncate(payload.Message, 512),
            ErrorMessage = Truncate(payload.ErrorMessage, 1024),
            FormatKey = Truncate(payload.FormatKey, 64)
        };

        return repository.SaveRemoteDispatchAttemptAsync(entity, cancellationToken);
    }

    private static Task HandleFrameExportAttemptAsync(ISkyMonitorTelemetryRepository repository, FrameExportAttemptPayload payload, CancellationToken cancellationToken)
    {
        var entity = new FrameExportAttemptEntity
        {
            AttemptedAtUtc = payload.AttemptedAtUtc,
            AttemptedAtLocal = payload.AttemptedAtLocal,
            FrameId = payload.FrameId,
            Stage = (int)payload.Stage,
            SinkName = Truncate(payload.SinkName, 128)!,
            Success = payload.Success,
            LatencyMilliseconds = payload.LatencyMilliseconds,
            PayloadBytes = payload.PayloadBytes,
            PayloadContentType = Truncate(payload.PayloadContentType, 128),
            PayloadExtension = Truncate(payload.PayloadExtension, 16),
            QueueLatencyMilliseconds = payload.QueueLatencyMilliseconds,
            ProcessingMilliseconds = payload.ProcessingMilliseconds,
            FullPipelineMilliseconds = payload.FullPipelineMilliseconds,
            FramesStacked = payload.FramesStacked,
            IntegrationMilliseconds = payload.IntegrationMilliseconds,
            ErrorMessage = Truncate(payload.ErrorMessage, 1024)
        };

        return repository.SaveFrameExportAttemptAsync(entity, cancellationToken);
    }

    private static Task HandleBackgroundStackerSampleAsync(ISkyMonitorTelemetryRepository repository, BackgroundStackerSamplePayload payload, CancellationToken cancellationToken)
    {
        var entity = new BackgroundStackerSampleEntity
        {
            CapturedAtUtc = payload.CapturedAtUtc,
            CapturedAtLocal = payload.CapturedAtLocal,
            QueueFillPercentage = payload.QueueFillPercentage,
            QueueDepth = payload.QueueDepth,
            QueueCapacity = payload.QueueCapacity,
            QueueLatencyMilliseconds = payload.QueueLatencyMilliseconds,
            StackDurationMilliseconds = payload.StackDurationMilliseconds,
            FilterDurationMilliseconds = payload.FilterDurationMilliseconds,
            QueuePressureLevel = payload.QueuePressureLevel,
            SecondsSinceLastCompleted = payload.SecondsSinceLastCompleted,
            QueueMemoryMegabytes = payload.QueueMemoryMegabytes
        };

        return repository.SaveBackgroundStackerSampleAsync(entity, cancellationToken);
    }

    private static Task HandleCapturePacingSampleAsync(ISkyMonitorTelemetryRepository repository, CapturePacingSamplePayload payload, CancellationToken cancellationToken)
    {
        var entity = new CapturePacingSampleEntity
        {
            CapturedAtUtc = payload.CapturedAtUtc,
            CapturedAtLocal = payload.CapturedAtLocal,
            Enabled = payload.Enabled,
            UsingBackgroundStacker = payload.UsingBackgroundStacker,
            BaseDelayMilliseconds = payload.BaseDelayMilliseconds,
            AdjustedDelayMilliseconds = payload.AdjustedDelayMilliseconds,
            QueuePressureLevel = payload.QueuePressureLevel,
            PressureAdditionalDelayMilliseconds = payload.PressureAdditionalDelayMilliseconds,
            PenaltyAdditionalDelayMilliseconds = payload.PenaltyAdditionalDelayMilliseconds,
            PenaltyActive = payload.PenaltyActive,
            PenaltyExpiresAtLocal = payload.PenaltyExpiresAtLocal
        };

        return repository.SaveCapturePacingSampleAsync(entity, cancellationToken);
    }

    private static Task HandleProcessingQueueSampleAsync(ISkyMonitorTelemetryRepository repository, ProcessingQueueSamplePayload payload, CancellationToken cancellationToken)
    {
        var entity = new ProcessingQueueSampleEntity
        {
            CapturedAtUtc = payload.CapturedAtUtc,
            CapturedAtLocal = payload.CapturedAtLocal,
            Enabled = payload.Enabled,
            Capacity = payload.Capacity,
            Depth = payload.Depth,
            BackpressureEvents = payload.BackpressureEvents,
            LastEnqueueWaitMilliseconds = payload.LastEnqueueWaitMilliseconds,
            PeakEnqueueWaitMilliseconds = payload.PeakEnqueueWaitMilliseconds,
            AverageEnqueueWaitMilliseconds = payload.AverageEnqueueWaitMilliseconds,
            LastProcessingMilliseconds = payload.LastProcessingMilliseconds,
            PeakProcessingMilliseconds = payload.PeakProcessingMilliseconds,
            AverageProcessingMilliseconds = payload.AverageProcessingMilliseconds
        };

        return repository.SaveProcessingQueueSampleAsync(entity, cancellationToken);
    }

    private static Task HandleFilterMetricSampleAsync(ISkyMonitorTelemetryRepository repository, FilterMetricSamplePayload payload, CancellationToken cancellationToken)
    {
        var entity = new FilterMetricSampleEntity
        {
            CapturedAtUtc = payload.CapturedAtUtc,
            CapturedAtLocal = payload.CapturedAtLocal,
            FilterName = Truncate(payload.FilterName, 128)!,
            AppliedCount = payload.AppliedCount,
            LastDurationMilliseconds = payload.LastDurationMilliseconds,
            AverageDurationMilliseconds = payload.AverageDurationMilliseconds
        };

        return repository.SaveFilterMetricSampleAsync(entity, cancellationToken);
    }

    private static Task HandleTelemetryEventAsync(ISkyMonitorTelemetryRepository repository, TelemetryEventPayload payload, CancellationToken cancellationToken)
    {
        var entity = new TelemetryEventEntity
        {
            OccurredAtUtc = payload.OccurredAtUtc,
            OccurredAtLocal = payload.OccurredAtLocal,
            Category = Truncate(payload.Category, 128)!,
            EventType = Truncate(payload.EventType, 64)!,
            Severity = Truncate(payload.Severity, 32)!,
            Summary = Truncate(payload.Summary, 512),
            Detail = Truncate(payload.Detail, 4096),
            PropertiesJson = payload.PropertiesJson
        };

        return repository.SaveTelemetryEventAsync(entity, cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..maxLength];
    }
}
