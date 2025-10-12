using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Telemetry.Entities;
using HVO.SkyMonitorV5.Data.Telemetry.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal sealed class SkyMonitorTelemetryIngestionService : BackgroundService
{
    private readonly ISkyMonitorTelemetryIngestionQueue _queue;
    private readonly ISkyMonitorTelemetryRepository _repository;
    private readonly ILogger<SkyMonitorTelemetryIngestionService> _logger;

    public SkyMonitorTelemetryIngestionService(
        ISkyMonitorTelemetryIngestionQueue queue,
        ISkyMonitorTelemetryRepository repository,
        ILogger<SkyMonitorTelemetryIngestionService> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
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
        switch (workItem)
        {
            case TelemetryWorkItem.RemoteDispatchAttempt(var payload):
                await HandleRemoteDispatchAttemptAsync(payload, cancellationToken).ConfigureAwait(false);
                break;
            case TelemetryWorkItem.BackgroundStackerSample(var payload):
                await HandleBackgroundStackerSampleAsync(payload, cancellationToken).ConfigureAwait(false);
                break;
            case TelemetryWorkItem.CapturePacingSample(var payload):
                await HandleCapturePacingSampleAsync(payload, cancellationToken).ConfigureAwait(false);
                break;
            case TelemetryWorkItem.ProcessingQueueSample(var payload):
                await HandleProcessingQueueSampleAsync(payload, cancellationToken).ConfigureAwait(false);
                break;
            case TelemetryWorkItem.FilterMetricSample(var payload):
                await HandleFilterMetricSampleAsync(payload, cancellationToken).ConfigureAwait(false);
                break;
            case TelemetryWorkItem.TelemetryEvent(var payload):
                await HandleTelemetryEventAsync(payload, cancellationToken).ConfigureAwait(false);
                break;
            default:
                _logger.LogWarning("Received unknown telemetry work item type {WorkItemType}.", workItem.GetType().Name);
                break;
        }
    }

    private Task HandleRemoteDispatchAttemptAsync(RemoteDispatchAttemptPayload payload, CancellationToken cancellationToken)
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

        return _repository.SaveRemoteDispatchAttemptAsync(entity, cancellationToken);
    }

    private Task HandleBackgroundStackerSampleAsync(BackgroundStackerSamplePayload payload, CancellationToken cancellationToken)
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

        return _repository.SaveBackgroundStackerSampleAsync(entity, cancellationToken);
    }

    private Task HandleCapturePacingSampleAsync(CapturePacingSamplePayload payload, CancellationToken cancellationToken)
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

        return _repository.SaveCapturePacingSampleAsync(entity, cancellationToken);
    }

    private Task HandleProcessingQueueSampleAsync(ProcessingQueueSamplePayload payload, CancellationToken cancellationToken)
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

        return _repository.SaveProcessingQueueSampleAsync(entity, cancellationToken);
    }

    private Task HandleFilterMetricSampleAsync(FilterMetricSamplePayload payload, CancellationToken cancellationToken)
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

        return _repository.SaveFilterMetricSampleAsync(entity, cancellationToken);
    }

    private Task HandleTelemetryEventAsync(TelemetryEventPayload payload, CancellationToken cancellationToken)
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

        return _repository.SaveTelemetryEventAsync(entity, cancellationToken);
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
