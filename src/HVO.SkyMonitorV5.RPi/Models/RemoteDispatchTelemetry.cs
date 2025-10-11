using System;
using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents measured metrics for a single remote dispatch attempt.
/// </summary>
public sealed record RemoteDispatchEventMetrics(
    double? LatencyMilliseconds,
    long? PayloadBytes,
    string? PayloadContentType,
    string? PayloadFileExtension)
{
    public static RemoteDispatchEventMetrics Empty { get; } = new(null, null, null, null);
}

/// <summary>
/// Aggregated counts for payload formats observed within the telemetry history window.
/// </summary>
public sealed record RemoteDispatchFormatSummary(string FormatKey, int Count);

/// <summary>
/// Aggregated telemetry snapshot for the remote dispatch pipeline.
/// </summary>
public sealed record RemoteDispatchMetricsSnapshot(
    DateTimeOffset GeneratedAt,
    int SampleCount,
    int SuccessCount,
    int FailureCount,
    int SkippedCount,
    double SuccessRatePercent,
    double? AverageLatencyMilliseconds,
    double? PeakLatencyMilliseconds,
    double? LastLatencyMilliseconds,
    long? LastPayloadBytes,
    string? LastPayloadContentType,
    string? LastPayloadExtension,
    IReadOnlyList<RemoteDispatchFormatSummary> FormatCounts);

/// <summary>
/// Historical sample for a single remote dispatch attempt used for visualization.
/// </summary>
public sealed record RemoteDispatchHistorySample(
    DateTimeOffset Timestamp,
    RemoteDispatchOutcome Outcome,
    string Mode,
    double? LatencyMilliseconds,
    long? PayloadBytes,
    string? PayloadContentType,
    string? PayloadExtension,
    string? Message,
    string? ErrorMessage);

/// <summary>
/// Container for remote dispatch telemetry history samples.
/// </summary>
public sealed record RemoteDispatchHistoryResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<RemoteDispatchHistorySample> Samples);
