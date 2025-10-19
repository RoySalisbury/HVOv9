using System;

namespace HVO.SkyMonitorV5.Data.Configurations.Entities;

/// <summary>
/// Represents an arbitrary configuration document stored as JSON within the SkyMonitor configuration database.
/// </summary>
public sealed class SystemSettingEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Distinct logical key for the configuration document.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Serialized JSON payload containing the configuration data.
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp for the last update applied to this document.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; set; }
        = DateTimeOffset.UtcNow;

    /// <summary>
    /// Monotonically increasing revision number raised each time the setting is updated.
    /// </summary>
    public long Revision { get; set; }
}
