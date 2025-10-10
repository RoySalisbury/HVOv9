using System;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

internal sealed class FilterTelemetryEntry
{
    private long _appliedCount;
    private double _totalDurationMs;
    private double? _lastDurationMs;

    public string FilterName { get; }

    public FilterTelemetryEntry(string filterName)
    {
        FilterName = filterName ?? throw new ArgumentNullException(nameof(filterName));
    }

    public void Record(double durationMilliseconds)
    {
        _appliedCount++;
        _totalDurationMs += durationMilliseconds;
        _lastDurationMs = durationMilliseconds;
    }

    public FilterTelemetrySnapshot Snapshot()
    {
        double? average = _appliedCount > 0 ? _totalDurationMs / _appliedCount : null;
        return new FilterTelemetrySnapshot(FilterName, _appliedCount, _lastDurationMs, average);
    }
}

internal sealed record FilterTelemetrySnapshot(string FilterName, long AppliedCount, double? LastDurationMilliseconds, double? AverageDurationMilliseconds);
