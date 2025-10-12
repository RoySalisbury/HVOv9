using System;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal abstract record TelemetryWorkItem(DateTimeOffset EnqueuedAtUtc)
{
    internal sealed record RemoteDispatchAttempt(DateTimeOffset EnqueuedAtUtc, RemoteDispatchAttemptPayload Payload) : TelemetryWorkItem(EnqueuedAtUtc);

    internal sealed record BackgroundStackerSample(DateTimeOffset EnqueuedAtUtc, BackgroundStackerSamplePayload Payload) : TelemetryWorkItem(EnqueuedAtUtc);

    internal sealed record CapturePacingSample(DateTimeOffset EnqueuedAtUtc, CapturePacingSamplePayload Payload) : TelemetryWorkItem(EnqueuedAtUtc);

    internal sealed record ProcessingQueueSample(DateTimeOffset EnqueuedAtUtc, ProcessingQueueSamplePayload Payload) : TelemetryWorkItem(EnqueuedAtUtc);

    internal sealed record FilterMetricSample(DateTimeOffset EnqueuedAtUtc, FilterMetricSamplePayload Payload) : TelemetryWorkItem(EnqueuedAtUtc);

    internal sealed record TelemetryEvent(DateTimeOffset EnqueuedAtUtc, TelemetryEventPayload Payload) : TelemetryWorkItem(EnqueuedAtUtc);
}

internal sealed record RemoteDispatchAttemptPayload(
    DateTimeOffset AttemptedAtUtc,
    DateTimeOffset AttemptedAtLocal,
    string Mode,
    RemoteDispatchOutcome Outcome,
    double? LatencyMilliseconds,
    long? PayloadBytes,
    string? PayloadContentType,
    string? PayloadExtension,
    string? Message,
    string? ErrorMessage,
    string? FormatKey);

internal sealed record BackgroundStackerSamplePayload(
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset CapturedAtLocal,
    double QueueFillPercentage,
    int QueueDepth,
    int QueueCapacity,
    double? QueueLatencyMilliseconds,
    double? StackDurationMilliseconds,
    double? FilterDurationMilliseconds,
    int QueuePressureLevel,
    double? SecondsSinceLastCompleted,
    double QueueMemoryMegabytes);

internal sealed record CapturePacingSamplePayload(
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset CapturedAtLocal,
    bool Enabled,
    bool UsingBackgroundStacker,
    int BaseDelayMilliseconds,
    int AdjustedDelayMilliseconds,
    int QueuePressureLevel,
    int PressureAdditionalDelayMilliseconds,
    int PenaltyAdditionalDelayMilliseconds,
    bool PenaltyActive,
    DateTimeOffset? PenaltyExpiresAtLocal);

internal sealed record ProcessingQueueSamplePayload(
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset CapturedAtLocal,
    bool Enabled,
    int Capacity,
    int Depth,
    int BackpressureEvents,
    double LastEnqueueWaitMilliseconds,
    double PeakEnqueueWaitMilliseconds,
    double AverageEnqueueWaitMilliseconds,
    double LastProcessingMilliseconds,
    double PeakProcessingMilliseconds,
    double AverageProcessingMilliseconds);

internal sealed record FilterMetricSamplePayload(
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset CapturedAtLocal,
    string FilterName,
    long AppliedCount,
    double? LastDurationMilliseconds,
    double? AverageDurationMilliseconds);

internal sealed record TelemetryEventPayload(
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset OccurredAtLocal,
    string Category,
    string EventType,
    string Severity,
    string? Summary,
    string? Detail,
    string? PropertiesJson);
