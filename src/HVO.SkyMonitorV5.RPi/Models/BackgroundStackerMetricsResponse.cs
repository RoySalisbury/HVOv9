using System;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents real-time metrics for the background stacker suitable for diagnostics dashboards.
/// </summary>
public sealed record BackgroundStackerMetricsResponse(
    bool Enabled,
    int QueueDepth,
    int QueueCapacity,
    double QueueFillPercentage,
    int PeakQueueDepth,
    double PeakQueueFillPercentage,
    long ProcessedFrameCount,
    long DroppedFrameCount,
    int QueuePressureLevel,
    double? LastQueueLatencyMilliseconds,
    double? AverageQueueLatencyMilliseconds,
    double? MaxQueueLatencyMilliseconds,
    double? LastStackMilliseconds,
    double? AverageStackMilliseconds,
    double? LastFilterMilliseconds,
    double? AverageFilterMilliseconds,
    long QueueMemoryBytes,
    long PeakQueueMemoryBytes,
    double QueueMemoryMegabytes,
    double PeakQueueMemoryMegabytes,
    DateTimeOffset? LastEnqueuedAt,
    DateTimeOffset? LastCompletedAt,
    double? SecondsSinceLastCompleted,
    int? LastFrameNumber);
