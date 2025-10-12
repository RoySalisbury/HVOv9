using System;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal readonly record struct TelemetryMetricsSnapshot(
    double QueueDepth,
    double LastIngestionLatencyMilliseconds,
    double LastRetentionDurationMilliseconds);

internal sealed record TelemetryRetentionSnapshot(
    DateTimeOffset? LastStartedAtUtc,
    DateTimeOffset? LastCompletedAtUtc,
    TimeSpan? LastDuration,
    int RemoteDispatchPurged,
    int BackgroundStackerPurged,
    int CapturePacingPurged,
    int ProcessingQueuePurged,
    int FilterMetricsPurged,
    int TelemetryEventsPurged,
    int TotalPurged,
    bool VacuumAttempted,
    bool VacuumSucceeded)
{
    public static TelemetryRetentionSnapshot Empty { get; } = new TelemetryRetentionSnapshot(
        LastStartedAtUtc: null,
        LastCompletedAtUtc: null,
        LastDuration: null,
        RemoteDispatchPurged: 0,
        BackgroundStackerPurged: 0,
        CapturePacingPurged: 0,
        ProcessingQueuePurged: 0,
        FilterMetricsPurged: 0,
        TelemetryEventsPurged: 0,
        TotalPurged: 0,
        VacuumAttempted: false,
        VacuumSucceeded: false);
}
