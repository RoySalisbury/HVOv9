using System;
using HVO.SkyMonitorV5.RPi.Infrastructure;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Infrastructure;

/// <summary>
/// Simple clock implementation for BenchmarkDotNet scenarios that relies on the system clock
/// without requiring the full observatory clock infrastructure.
/// </summary>
public sealed class BenchmarkObservatoryClock : IObservatoryClock
{
    private readonly TimeZoneInfo _timeZone;
    private readonly string _timeZoneDisplayName;

    public BenchmarkObservatoryClock(string? timeZoneId)
    {
        (_timeZone, _timeZoneDisplayName) = ResolveTimeZone(timeZoneId);
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset LocalNow => ToLocal(UtcNow);

    public TimeZoneInfo TimeZone => _timeZone;

    public string TimeZoneDisplayName => _timeZoneDisplayName;

    public event EventHandler? TimeZoneChanged
    {
        add { }
        remove { }
    }

    public string GetZoneLabel(DateTimeOffset localTime)
    {
        try
        {
            if (_timeZone.Equals(TimeZoneInfo.Utc))
            {
                return "UTC";
            }

            var isDst = _timeZone.IsDaylightSavingTime(localTime);
            var label = isDst ? _timeZone.DaylightName : _timeZone.StandardName;
            return string.IsNullOrWhiteSpace(label) ? _timeZone.Id : label;
        }
        catch (InvalidTimeZoneException)
        {
            return _timeZone.Id;
        }
        catch (ArgumentException)
        {
            return _timeZone.Id;
        }
    }

    public DateTimeOffset ToLocal(DateTimeOffset timestamp)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(timestamp, _timeZone);
        }
        catch (InvalidTimeZoneException)
        {
            return timestamp;
        }
        catch (ArgumentException)
        {
            return timestamp;
        }
    }

    private static (TimeZoneInfo TimeZone, string DisplayName) ResolveTimeZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return (tz, tz.DisplayName);
            }
            catch (TimeZoneNotFoundException)
            {
                // Fall back to UTC if the configured time zone cannot be resolved on the benchmark host.
            }
            catch (InvalidTimeZoneException)
            {
                // Fall back to UTC if the configured time zone is invalid.
            }
        }

        return (TimeZoneInfo.Utc, "Coordinated Universal Time (UTC)");
    }
}
