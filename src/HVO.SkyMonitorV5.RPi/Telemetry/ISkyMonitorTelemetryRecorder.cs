using System;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

public interface ISkyMonitorTelemetryRecorder
{
    void RecordRemoteDispatchAttempt(
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
        string? formatKey);

    void RecordFrameExportAttempt(
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
        string? errorMessage);

    void RecordBackgroundStackerSample(DateTimeOffset capturedAtUtc, BackgroundStackerHistorySample sample);

    void RecordCapturePacingSample(DateTimeOffset capturedAtUtc, CapturePacingStatus localizedStatus);

    void RecordProcessingQueueSample(DateTimeOffset capturedAtUtc, ProcessingQueueStatus localizedStatus);

    void RecordFilterMetricSample(string filterName, long appliedCount, double? lastDurationMilliseconds, double? averageDurationMilliseconds);

    void RecordTelemetryEvent(DateTimeOffset occurredAtUtc, DateTimeOffset occurredAtLocal, string category, string eventType, string severity, string? summary, string? detail, string? propertiesJson);
}
