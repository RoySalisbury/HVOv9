using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.Data.Catalogs.DeepSky;

/// <summary>
/// Represents a curated deep-sky target baked into the SkyMonitor catalog.
/// </summary>
public sealed class DeepSkyObjectEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Primary catalog identifier (e.g., Messier, NGC, IC).
    /// </summary>
    [MaxLength(32)]
    public string PrimaryId { get; set; } = string.Empty;

    /// <summary>
    /// Optional familiar/common name for UI display.
    /// </summary>
    [MaxLength(128)]
    public string? CommonName { get; set; }

    /// <summary>
    /// Associated constellation in IAU 3-letter format (e.g., AND, ORI).
    /// </summary>
    [MaxLength(3)]
    public string? Constellation { get; set; }

    public double RightAscensionHours { get; set; }

    public double DeclinationDegrees { get; set; }

    public double? ApparentMagnitude { get; set; }

    /// <summary>
    /// Classification (Galaxy, Nebula, OpenCluster, etc.).
    /// </summary>
    [MaxLength(32)]
    public string? ObjectType { get; set; }
}
