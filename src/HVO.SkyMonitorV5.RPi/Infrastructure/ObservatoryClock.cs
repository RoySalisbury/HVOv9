using System;
using System.Runtime.InteropServices;
using System.Threading;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Infrastructure;

/// <summary>
/// Application-wide clock service that exposes observatory-local timestamps while honouring configuration updates.
/// </summary>
public sealed class ObservatoryClock : IObservatoryClock, IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ObservatoryClock>? _logger;
    private readonly IDisposable _optionsSubscription = null!;
    private readonly object _syncRoot = new();

    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;
    private string _timeZoneDisplayName = "Coordinated Universal Time (UTC)";
    private string _requestedTimeZoneId = TimeZoneInfo.Utc.Id;

    public event EventHandler? TimeZoneChanged;

    public ObservatoryClock(
        TimeProvider timeProvider,
        IOptionsMonitor<ObservatoryLocationOptions> locationOptions,
        ILogger<ObservatoryClock>? logger = null)
    {
        _timeProvider = timeProvider;
        _logger = logger;

    var currentOptions = locationOptions.CurrentValue ?? throw new InvalidOperationException("Observatory location options are not configured.");
    ApplyOptions(currentOptions);
    _optionsSubscription = locationOptions.OnChange(ApplyOptions) ?? throw new InvalidOperationException("Unable to subscribe to observatory location option changes.");
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public DateTimeOffset LocalNow
    {
        get
        {
            var utc = UtcNow;
            var tz = TimeZone;

            try
            {
                return TimeZoneInfo.ConvertTime(utc, tz);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidTimeZoneException)
            {
                _logger?.LogWarning(ex,
                    "Unable to convert UTC time to observatory time zone {TimeZoneId}; returning UTC.",
                    tz.Id);
                return utc;
            }
        }
    }

    public TimeZoneInfo TimeZone
    {
        get
        {
            lock (_syncRoot)
            {
                return _timeZone;
            }
        }
    }

    public string TimeZoneDisplayName
    {
        get
        {
            lock (_syncRoot)
            {
                return _timeZoneDisplayName;
            }
        }
    }

    public string GetZoneLabel(DateTimeOffset localTime)
    {
        var tz = TimeZone;

        if (tz.Equals(TimeZoneInfo.Utc))
        {
            return "UTC";
        }

        try
        {
            var isDst = tz.IsDaylightSavingTime(localTime);
            var name = isDst ? tz.DaylightName : tz.StandardName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (ArgumentException ex)
        {
            _logger?.LogWarning(ex,
                "Failed to determine daylight-saving status for time zone {TimeZoneId}.", tz.Id);
        }

        return tz.Id;
    }

    public DateTimeOffset ToLocal(DateTimeOffset timestamp)
    {
        var tz = TimeZone;

        try
        {
            return TimeZoneInfo.ConvertTime(timestamp, tz);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidTimeZoneException)
        {
            _logger?.LogWarning(ex,
                "Unable to convert timestamp {Timestamp:o} to observatory time zone {TimeZoneId}; returning original value.",
                timestamp,
                tz.Id);
            return timestamp;
        }
    }

    private void ApplyOptions(ObservatoryLocationOptions options)
    {
        var timeZoneId = options.TimeZoneId;
        var (resolved, displayName, requestedId) = ResolveTimeZone(timeZoneId);

        lock (_syncRoot)
        {
            _timeZone = resolved;
            _timeZoneDisplayName = displayName;
            _requestedTimeZoneId = requestedId;
        }

        TimeZoneChanged?.Invoke(this, EventArgs.Empty);
    }

    private (TimeZoneInfo TimeZone, string DisplayName, string RequestedId) ResolveTimeZone(string? configuredId)
    {
        if (string.IsNullOrWhiteSpace(configuredId))
        {
            _logger?.LogWarning("No time zone configured for the observatory; defaulting to UTC.");
            return (TimeZoneInfo.Utc, "Coordinated Universal Time (UTC)", TimeZoneInfo.Utc.Id);
        }

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(configuredId);
            return (tz, tz.DisplayName, configuredId);
        }
        catch (TimeZoneNotFoundException)
        {
            return ResolveWithAlternateIdentifiers(configuredId);
        }
        catch (InvalidTimeZoneException ex)
        {
            _logger?.LogWarning(ex,
                "Configured observatory time zone {TimeZoneId} is invalid; defaulting to UTC.",
                configuredId);
            return (TimeZoneInfo.Utc, $"Invalid time zone ({configuredId}); defaulting to UTC", configuredId);
        }
    }

    private (TimeZoneInfo TimeZone, string DisplayName, string RequestedId) ResolveWithAlternateIdentifiers(string configuredId)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(configuredId, out var windowsId))
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                    return (tz, $"{configuredId} ({tz.DisplayName})", configuredId);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    _logger?.LogWarning(ex,
                        "Failed to resolve converted Windows time zone {WindowsId} for original identifier {TimeZoneId}.",
                        windowsId, configuredId);
                }
            }
        }
        else
        {
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(configuredId, out var ianaId))
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
                    return (tz, tz.DisplayName, configuredId);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    _logger?.LogWarning(ex,
                        "Failed to resolve converted IANA time zone {IanaId} for original identifier {TimeZoneId}.",
                        ianaId, configuredId);
                }
            }
        }

        _logger?.LogWarning(
            "Time zone {TimeZoneId} was not found on this system; defaulting to UTC.", configuredId);
        return (TimeZoneInfo.Utc, $"Unrecognised time zone ({configuredId}); defaulting to UTC", configuredId);
    }

    public void Dispose()
    {
        _optionsSubscription.Dispose();
    }
}
