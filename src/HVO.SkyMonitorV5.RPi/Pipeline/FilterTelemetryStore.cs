using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

internal sealed class FilterTelemetryStore
{
    private readonly ConcurrentDictionary<string, FilterTelemetryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string filterName, double durationMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(filterName))
        {
            return;
        }

        var entry = _entries.GetOrAdd(filterName, name => new FilterTelemetryEntry(name));
        entry.Record(durationMilliseconds);
    }

    public FilterMetricsSnapshot Snapshot()
    {
        var snapshots = _entries.Values
            .Select(entry => entry.Snapshot())
            .OrderBy(entry => entry.FilterName)
            .Select(entry => new FilterMetrics(
                entry.FilterName,
                entry.AppliedCount,
                entry.LastDurationMilliseconds,
                entry.AverageDurationMilliseconds))
            .ToArray();

        return new FilterMetricsSnapshot(snapshots);
    }
}
