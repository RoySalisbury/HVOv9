namespace HVO.SkyMonitorV5.Data.Configurations.Entities;

/// <summary>
/// Represents an observatory site with geolocation metadata.
/// </summary>
public sealed class ObservatorySiteEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Stable slug identifier used by the runtime to reference this site.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for the observatory.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Latitude in decimal degrees. Positive values represent the northern hemisphere.
    /// </summary>
    public double LatitudeDegrees { get; set; }

    /// <summary>
    /// Longitude in decimal degrees. Positive values represent the eastern hemisphere.
    /// </summary>
    public double LongitudeDegrees { get; set; }

    /// <summary>
    /// Time zone identifier (IANA/Windows) used for local time conversions.
    /// </summary>
    public string TimeZoneId { get; set; } = string.Empty;

    /// <summary>
    /// Monotonically increasing revision number used to track configuration updates.
    /// </summary>
    public long Revision { get; set; }
}
