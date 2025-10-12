using System;
using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Models;

public sealed record DataStoreMetricsSnapshot(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset GeneratedAtLocal,
    DataStoreInstanceMetrics ConfigurationStore,
    DataStoreInstanceMetrics TelemetryStore);

public sealed record DataStoreInstanceMetrics(
    string DatabasePath,
    bool Exists,
    long? FileBytes,
    double? FileMegabytes,
    long? PageCount,
    long? PageSizeBytes,
    long? FreePages,
    IReadOnlyList<DataStoreTableMetric> Tables,
    DataStoreBootstrapStatusMetrics Bootstrap,
    TelemetryIngestionMetricsSummary? TelemetryIngestion,
    TelemetryRetentionSummaryMetrics? TelemetryRetention);

public sealed record DataStoreTableMetric(string Table, long RowCount);

public sealed record DataStoreBootstrapStatusMetrics(
    bool Ran,
    bool Succeeded,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorMessage);

public sealed record TelemetryIngestionMetricsSummary(
    double QueueDepth,
    double LastIngestionLatencyMilliseconds,
    double LastRetentionDurationMilliseconds);

public sealed record TelemetryRetentionSummaryMetrics(
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
    bool VacuumSucceeded);
