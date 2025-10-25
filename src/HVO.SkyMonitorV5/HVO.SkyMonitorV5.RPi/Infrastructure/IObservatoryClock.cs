using System;

namespace HVO.SkyMonitorV5.RPi.Infrastructure;

/// <summary>
/// Provides observatory-aware time utilities that return both UTC and localised timestamps.
/// </summary>
public interface IObservatoryClock
{
    /// <summary>
    /// Gets the current UTC timestamp.
    /// </summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Gets the current observatory-local timestamp.
    /// </summary>
    DateTimeOffset LocalNow { get; }

    /// <summary>
    /// Gets the resolved time-zone information for the observatory.
    /// </summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>
    /// Human-readable display name for the observatory time zone.
    /// </summary>
    string TimeZoneDisplayName { get; }

    /// <summary>
    /// Gets a human-friendly label for the supplied local time (for example, "MST" or the configured time-zone identifier).
    /// </summary>
    /// <param name="localTime">A timestamp expressed in the observatory time zone.</param>
    /// <returns>Zone label suited for UI display.</returns>
    string GetZoneLabel(DateTimeOffset localTime);

    /// <summary>
    /// Converts the supplied timestamp into the observatory-local time zone.
    /// </summary>
    /// <param name="timestamp">A timestamp expressed in any offset.</param>
    /// <returns>The timestamp converted to the observatory-local offset.</returns>
    DateTimeOffset ToLocal(DateTimeOffset timestamp);

    /// <summary>
    /// Raised whenever the observatory time-zone configuration changes.
    /// </summary>
    event EventHandler? TimeZoneChanged;
}
