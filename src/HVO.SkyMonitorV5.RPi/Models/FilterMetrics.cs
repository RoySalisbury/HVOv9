using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents aggregated metrics for a specific frame filter.
/// </summary>
public sealed record FilterMetrics(
    string FilterName,
    long AppliedCount,
    double? LastDurationMilliseconds,
    double? AverageDurationMilliseconds);

/// <summary>
/// Container for the filter metrics collection.
/// </summary>
public sealed record FilterMetricsSnapshot(IReadOnlyList<FilterMetrics> Filters);
