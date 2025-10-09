using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configures how the catalog-backed star selection behaves for the mock fisheye camera.
/// </summary>
public sealed class StarCatalogOptions
{
    public const string SectionName = "StarCatalog";

    /// <summary>
    /// Gets or sets the faintest star magnitude that should be considered for rendering.
    /// </summary>
    [Range(-2.0, 15.0)]
    public double MagnitudeLimit { get; set; } = 6.5;

    /// <summary>
    /// Gets or sets the minimum altitude above the horizon (in degrees) that a star must reach at any time to be considered.
    /// </summary>
    [Range(0.0, 90.0)]
    public double MinMaxAltitudeDegrees { get; set; } = 10.0;

    /// <summary>
    /// Gets or sets the total number of stars to render from the catalog for the current frame.
    /// </summary>
    [Range(10, 2000)]
    public int TopStarCount { get; set; } = 300;

    /// <summary>
    /// Gets or sets a value indicating whether the selection should be stratified across RA/Dec buckets for even sky coverage.
    /// </summary>
    public bool StratifiedSelection { get; set; }
        = false;

    /// <summary>
    /// Gets or sets a value indicating whether visible planets (Mercury through Saturn) should be rendered alongside catalog stars.
    /// </summary>
    public bool IncludePlanets { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the Moon should be rendered alongside catalog stars.
    /// </summary>
    public bool IncludeMoon { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the outer planets (Uranus and Neptune) should be rendered when planets are enabled.
    /// </summary>
    public bool IncludeOuterPlanets { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the Sun should be rendered in daytime simulation scenarios.
    /// </summary>
    public bool IncludeSun { get; set; } = false;

    /// <summary>
    /// Gets or sets the number of right ascension bins to use when stratified selection is enabled.
    /// </summary>
    [Range(4, 72)]
    public int RightAscensionBins { get; set; } = 24;

    /// <summary>
    /// Gets or sets the number of declination bands to use when stratified selection is enabled.
    /// </summary>
    [Range(2, 24)]
    public int DeclinationBands { get; set; } = 8;
}
