using System;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal sealed class SkyMonitorTelemetryRecorder : ISkyMonitorTelemetryRecorder
{
    private readonly ISkyMonitorTelemetryIngestionQueue _queue;
    private readonly IObservatoryClock _clock;
    private readonly ILogger<SkyMonitorTelemetryRecorder> _logger;

    public SkyMonitorTelemetryRecorder(
        ISkyMonitorTelemetryIngestionQueue queue,
        IObservatoryClock clock,
        ILogger<SkyMonitorTelemetryRecorder> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RecordRemoteDispatchAttempt(
        DateTimeOffset attemptedAtUtc,
        DateTimeOffset attemptedAtLocal,
        string mode,
        RemoteDispatchOutcome outcome,
        double? latencyMilliseconds,
        long? payloadBytes,
        string? payloadContentType,
        string? payloadExtension,
        string? message,
        string? errorMessage,
        string? formatKey)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return;
        }

        var payload = new RemoteDispatchAttemptPayload(
            attemptedAtUtc,
            attemptedAtLocal,
            mode,
            outcome,
            latencyMilliseconds,
            payloadBytes,
            payloadContentType,
            payloadExtension,
            message,
            errorMessage,
            formatKey);

        var enqueuedAtUtc = _clock.UtcNow;

        if (!_queue.TryWrite(new TelemetryWorkItem.RemoteDispatchAttempt(enqueuedAtUtc, payload)))
        {
            _logger.LogWarning("Telemetry queue saturated; dropping remote dispatch attempt telemetry for mode {Mode}.", mode);
        }
    }

    public void RecordFrameExportAttempt(
        DateTimeOffset attemptedAtUtc,
        DateTimeOffset attemptedAtLocal,
        Guid frameId,
        FrameExportStage stage,
        string sinkName,
        bool success,
        double? latencyMilliseconds,
        long? payloadBytes,
        string? payloadContentType,
        string? payloadExtension,
        double? queueLatencyMilliseconds,
        double? processingMilliseconds,
        double? fullPipelineMilliseconds,
        int? framesStacked,
        int? integrationMilliseconds,
        string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(sinkName))
        {
            return;
        }

        var payload = new FrameExportAttemptPayload(
            attemptedAtUtc,
            attemptedAtLocal,
            frameId,
            stage,
            sinkName,
            success,
            latencyMilliseconds,
            payloadBytes,
            payloadContentType,
            payloadExtension,
            queueLatencyMilliseconds,
            processingMilliseconds,
            fullPipelineMilliseconds,
            framesStacked,
            integrationMilliseconds,
            errorMessage);

        var enqueuedAtUtc = _clock.UtcNow;

        if (!_queue.TryWrite(new TelemetryWorkItem.FrameExportAttempt(enqueuedAtUtc, payload)))
        {
            _logger.LogWarning(
                "Telemetry queue saturated; dropping frame export attempt telemetry for sink {SinkName}.",
                sinkName);
        }
    }

    public void RecordBackgroundStackerSample(DateTimeOffset capturedAtUtc, BackgroundStackerHistorySample sample)
    {
        if (sample is null)
        {
            throw new ArgumentNullException(nameof(sample));
        }

        var payload = new BackgroundStackerSamplePayload(
            capturedAtUtc,
            sample.Timestamp,
            sample.QueueFillPercentage,
            sample.QueueDepth,
            sample.QueueCapacity,
            sample.QueueLatencyMilliseconds,
            sample.StackDurationMilliseconds,
            sample.FilterDurationMilliseconds,
            sample.QueuePressureLevel,
            sample.SecondsSinceLastCompleted,
            sample.QueueMemoryMegabytes);

        var enqueuedAtUtc = _clock.UtcNow;

        if (!_queue.TryWrite(new TelemetryWorkItem.BackgroundStackerSample(enqueuedAtUtc, payload)))
        {
            _logger.LogWarning("Telemetry queue saturated; dropping background stacker sample.");
        }
    }

    public void RecordCapturePacingSample(DateTimeOffset capturedAtUtc, CapturePacingStatus localizedStatus)
    {
        var payload = new CapturePacingSamplePayload(
            capturedAtUtc,
            localizedStatus.Timestamp,
            localizedStatus.Enabled,
            localizedStatus.UsingBackgroundStacker,
            localizedStatus.BaseDelayMilliseconds,
            localizedStatus.AdjustedDelayMilliseconds,
            localizedStatus.QueuePressureLevel,
            localizedStatus.PressureAdditionalDelayMilliseconds,
            localizedStatus.PenaltyAdditionalDelayMilliseconds,
            localizedStatus.PenaltyActive,
            localizedStatus.PenaltyExpiresAt);

        var enqueuedAtUtc = _clock.UtcNow;

        if (!_queue.TryWrite(new TelemetryWorkItem.CapturePacingSample(enqueuedAtUtc, payload)))
        {
            _logger.LogWarning("Telemetry queue saturated; dropping capture pacing sample.");
        }
    }

    public void RecordProcessingQueueSample(DateTimeOffset capturedAtUtc, ProcessingQueueStatus localizedStatus)
    {
        var payload = new ProcessingQueueSamplePayload(
            capturedAtUtc,
            localizedStatus.Timestamp,
            localizedStatus.Enabled,
            localizedStatus.Capacity,
            localizedStatus.Depth,
            localizedStatus.BackpressureEvents,
            localizedStatus.LastEnqueueWaitMilliseconds,
            localizedStatus.PeakEnqueueWaitMilliseconds,
            localizedStatus.AverageEnqueueWaitMilliseconds,
            localizedStatus.LastProcessingMilliseconds,
            localizedStatus.PeakProcessingMilliseconds,
            localizedStatus.AverageProcessingMilliseconds);

        var enqueuedAtUtc = _clock.UtcNow;

        if (!_queue.TryWrite(new TelemetryWorkItem.ProcessingQueueSample(enqueuedAtUtc, payload)))
        {
            _logger.LogWarning("Telemetry queue saturated; dropping processing queue sample.");
        }
    }

    public void RecordFilterMetricSample(string filterName, long appliedCount, double? lastDurationMilliseconds, double? averageDurationMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(filterName))
        {
            return;
        }

        var payload = new FilterMetricSamplePayload(
            _clock.UtcNow,
            _clock.LocalNow,
            filterName,
            appliedCount,
            lastDurationMilliseconds,
            averageDurationMilliseconds);

        var enqueuedAtUtc = _clock.UtcNow;

        if (!_queue.TryWrite(new TelemetryWorkItem.FilterMetricSample(enqueuedAtUtc, payload)))
        {
            _logger.LogWarning("Telemetry queue saturated; dropping filter metric sample for {FilterName}.", filterName);
        }
    }

    public void RecordTelemetryEvent(DateTimeOffset occurredAtUtc, DateTimeOffset occurredAtLocal, string category, string eventType, string severity, string? summary, string? detail, string? propertiesJson)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(severity))
        {
            return;
        }

        var payload = new TelemetryEventPayload(
            occurredAtUtc,
            occurredAtLocal,
            category,
            eventType,
            severity,
            summary,
            detail,
            propertiesJson);

        var enqueuedAtUtc = _clock.UtcNow;

        if (!_queue.TryWrite(new TelemetryWorkItem.TelemetryEvent(enqueuedAtUtc, payload)))
        {
            _logger.LogWarning("Telemetry queue saturated; dropping telemetry event {Category}/{EventType}.", category, eventType);
        }
    }
}
