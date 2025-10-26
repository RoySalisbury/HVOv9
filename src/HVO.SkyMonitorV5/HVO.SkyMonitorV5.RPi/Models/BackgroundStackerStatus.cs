using System;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Captures runtime telemetry for the background frame stacker.
/// </summary>
public sealed record BackgroundStackerStatus(
    bool Enabled,
    int QueueDepth,
    int QueueCapacity,
    int PeakQueueDepth,
    long ProcessedFrameCount,
    long DroppedFrameCount,
    int? LastFrameNumber,
    DateTimeOffset? LastEnqueuedAt,
    DateTimeOffset? LastCompletedAt,
    double? LastQueueLatencyMilliseconds,
    double? AverageQueueLatencyMilliseconds,
    double? MaxQueueLatencyMilliseconds,
    double? LastStackMilliseconds,
    double? LastFilterMilliseconds,
    double? AverageStackMilliseconds,
    double? AverageFilterMilliseconds,
    long QueueMemoryBytes,
    long PeakQueueMemoryBytes,
    double QueueFillPercentage,
    double PeakQueueFillPercentage,
    double QueueMemoryMegabytes,
    double PeakQueueMemoryMegabytes,
    double? SecondsSinceLastCompleted,
    int QueuePressureLevel);
