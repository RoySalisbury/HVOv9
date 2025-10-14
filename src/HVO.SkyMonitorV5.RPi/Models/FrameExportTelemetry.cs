using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Exports;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Aggregated metrics for a specific frame export sink and stage combination.
/// </summary>
public sealed record FrameExportSinkMetrics(
    FrameExportStage Stage,
    string SinkName,
    int AttemptCount,
    int SuccessCount,
    int FailureCount,
    double SuccessRatePercent,
    double? AverageLatencyMilliseconds,
    double? AverageQueueLatencyMilliseconds,
    double? AverageProcessingMilliseconds,
    double? AverageFullPipelineMilliseconds,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastAttemptAtLocal,
    bool? LastAttemptSucceeded,
    double? LastAttemptLatencyMilliseconds,
    long? LastAttemptPayloadBytes,
    string? LastAttemptContentType,
    string? LastAttemptExtension,
    double? LastAttemptQueueLatencyMilliseconds,
    double? LastAttemptProcessingMilliseconds,
    double? LastAttemptFullPipelineMilliseconds,
    DateTimeOffset? LastSuccessAtUtc,
    DateTimeOffset? LastSuccessAtLocal,
    DateTimeOffset? LastFailureAtUtc,
    DateTimeOffset? LastFailureAtLocal,
    string? LastFailureMessage);

/// <summary>
/// Summary metrics for all frame export sinks.
/// </summary>
public sealed record FrameExportMetricsSnapshot(
    DateTimeOffset GeneratedAt,
    int TotalAttemptCount,
    int TotalSuccessCount,
    int TotalFailureCount,
    double SuccessRatePercent,
    IReadOnlyList<FrameExportSinkMetrics> Sinks,
    int PendingRetryCount,
    IReadOnlyList<FrameExportRetryEntry> PendingRetries);

/// <summary>
/// Telemetry sample for a single frame export attempt.
/// </summary>
public sealed record FrameExportHistorySample(
    Guid FrameId,
    DateTimeOffset AttemptedAtUtc,
    DateTimeOffset AttemptedAtLocal,
    FrameExportStage Stage,
    string SinkName,
    bool Success,
    double? LatencyMilliseconds,
    long? PayloadBytes,
    string? PayloadContentType,
    string? PayloadExtension,
    double? QueueLatencyMilliseconds,
    double? ProcessingMilliseconds,
    double? FullPipelineMilliseconds,
    int? FramesStacked,
    int? IntegrationMilliseconds,
    string? ErrorMessage);

/// <summary>
/// Container for recent frame export attempt samples.
/// </summary>
public sealed record FrameExportHistoryResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<FrameExportHistorySample> Attempts);

/// <summary>
/// Snapshot describing a pending frame export retry entry.
/// </summary>
public sealed record FrameExportRetryEntry(
    Guid FrameId,
    FrameExportStage Stage,
    string SinkName,
    int AttemptCount,
    DateTimeOffset EnqueuedAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset NextAttemptAtUtc,
    string? LastErrorMessage);
