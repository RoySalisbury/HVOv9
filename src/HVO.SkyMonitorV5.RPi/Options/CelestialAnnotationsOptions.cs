using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configuration for the celestial annotations overlay filter.
/// </summary>
public sealed class CelestialAnnotationsOptions
{
    public const string SectionName = "CameraPipeline:CelestialAnnotations";

    /// <summary>
    /// Gets the collection of star names that should receive labels when visible.
    /// </summary>
    public IList<string> StarNames { get; set; } = new List<string>();

    /// <summary>
    /// Gets the collection of planet names that should receive labels when visible.
    /// </summary>
    public IList<string> PlanetNames { get; set; } = new List<string>();

    /// <summary>
    /// Gets the collection of deep-sky objects to annotate.
    /// </summary>
    public IList<DeepSkyObjectOption> DeepSkyObjects { get; set; } = new List<DeepSkyObjectOption>();

    /// <summary>
    /// Gets or sets the font size (in pixels) used for annotation labels.
    /// </summary>
    [Range(4.0, 72.0)]
    public float LabelFontSize { get; set; } = 8.0f;

    /// <summary>
    /// Gets or sets the text colour used for star labels.
    /// Accepts hex strings in #RRGGBB or #AARRGGBB format.
    /// </summary>
    [StringLength(16)]
    public string StarLabelColor { get; set; } = "#EBF5FF";

    /// <summary>
    /// Gets or sets the text colour used for planet labels.
    /// </summary>
    [StringLength(16)]
    public string PlanetLabelColor { get; set; } = "#FFE8C5";

    /// <summary>
    /// Gets or sets the text colour used for deep-sky object labels.
    /// </summary>
    [StringLength(16)]
    public string DeepSkyLabelColor { get; set; } = "#F0E4FF";

    /// <summary>
    /// Gets or sets the radius (in pixels) of the locator ring drawn around star annotations.
    /// </summary>
    [Range(1.0, 64.0)]
    public float StarRingRadius { get; set; } = 3.0f;

    /// <summary>
    /// Gets or sets the radius (in pixels) of the locator ring drawn around planet annotations.
    /// </summary>
    [Range(1.0, 64.0)]
    public float PlanetRingRadius { get; set; } = 3.6f;

    /// <summary>
    /// Gets or sets the radius (in pixels) of the locator ring drawn around deep-sky annotations.
    /// </summary>
    [Range(1.0, 64.0)]
    public float DeepSkyRingRadius { get; set; } = 4.0f;
}

/// <summary>
/// Describes a deep-sky object that can be annotated on the fisheye projection.
/// </summary>
public sealed class DeepSkyObjectOption
{
    /// <summary>
    /// Gets or sets the friendly label that appears beside the marker.
    /// </summary>
    [Required]
    [StringLength(64)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the object's right ascension in sidereal hours (0-24).
    /// </summary>
    [Range(0.0, 24.0)]
    public double RightAscensionHours { get; set; }
        = 0.0;

    /// <summary>
    /// Gets or sets the object's declination in degrees (-90 to +90).
    /// </summary>
    [Range(-90.0, 90.0)]
    public double DeclinationDegrees { get; set; }
        = 0.0;

    /// <summary>
    /// Gets or sets the approximate integrated magnitude used to size the marker.
    /// </summary>
    [Range(-30.0, 30.0)]
    public double Magnitude { get; set; } = 8.0;

    /// <summary>
    /// Gets or sets an optional hexadecimal colour (e.g. "#8FB7FF") used for the annotation marker.
    /// </summary>
    [StringLength(16)]
    public string? Color { get; set; }
        = null;
}
