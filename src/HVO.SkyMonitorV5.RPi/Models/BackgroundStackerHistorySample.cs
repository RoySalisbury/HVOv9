using System;
using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents a historical snapshot of background stacker telemetry for visualization and analysis.
/// </summary>
public sealed record BackgroundStackerHistorySample(
    DateTimeOffset Timestamp,
    double QueueFillPercentage,
    int QueueDepth,
    int QueueCapacity,
    double? QueueLatencyMilliseconds,
    double? StackDurationMilliseconds,
    double? FilterDurationMilliseconds,
    int QueuePressureLevel,
    double? SecondsSinceLastCompleted,
    double QueueMemoryMegabytes);

/// <summary>
/// Container for the historical telemetry samples.
/// </summary>
public sealed record BackgroundStackerHistoryResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<BackgroundStackerHistorySample> Samples);
