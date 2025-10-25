using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Provides latitude and longitude configuration for the observatory site.
/// </summary>
public sealed class ObservatoryLocationOptions
{
    /// <summary>
    /// Observatory latitude in decimal degrees. Positive values represent the northern hemisphere.
    /// </summary>
    [Range(-90, 90)]
    public double LatitudeDegrees { get; set; } = 35.347;

    /// <summary>
    /// Observatory longitude in decimal degrees. Positive values represent the eastern hemisphere.
    /// West longitudes must be negative.
    /// </summary>
    [Range(-180, 180)]
    public double LongitudeDegrees { get; set; } = -113.878;

    /// <summary>
    /// IANA or Windows time-zone identifier representing the observatory's local time.
    /// Defaults to the Hualapai Valley Observatory time zone (America/Phoenix).
    /// </summary>
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string TimeZoneId { get; set; } = "America/Phoenix";
}
