using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace HVO.SkyMonitorV5.RPi.Tests.TestHelpers;

internal sealed class TestMeterFactory : IMeterFactory, IDisposable
{
    private readonly List<Meter> _meters = new();
    private readonly object _lock = new();

    public Meter Create(string name)
    {
        return Create(new MeterOptions(name));
    }

    public Meter Create(MeterOptions meterOptions)
    {
        if (meterOptions is null)
        {
            throw new ArgumentNullException(nameof(meterOptions));
        }

        var meter = new Meter(meterOptions.Name ?? "test", meterOptions.Version);
        lock (_lock)
        {
            _meters.Add(meter);
        }

        return meter;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }

            _meters.Clear();
        }
    }
}
