using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configuration for rendering constellation line figures on top of the stacked frame.
/// </summary>
public sealed class ConstellationFigureOptions
{
    public const string SectionName = "CameraPipeline:ConstellationFigures";

    /// <summary>
    /// Gets the collection of constellation display names that should be rendered.
    /// When empty, all constellations from the catalog will be considered.
    /// </summary>
    public IList<string> Constellations { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the stroke thickness, in pixels, used when drawing constellation lines.
    /// </summary>
    [Range(0.05, 8.0)]
    public float LineThickness { get; set; } = 1.6f;

    /// <summary>
    /// Gets or sets the opacity applied to the constellation line colour.
    /// </summary>
    [Range(0.05, 1.0)]
    public float LineOpacity { get; set; } = 0.75f;

    /// <summary>
    /// Gets or sets the base colour used for constellation lines.
    /// Accepts hex strings in #RRGGBB or #AARRGGBB format.
    /// </summary>
    [StringLength(16)]
    public string LineColor { get; set; } = "#7FB2FF";

    /// <summary>
    /// Gets or sets a value indicating whether the constellation lines should use a dashed style.
    /// </summary>
    public bool UseDashedLine { get; set; } = false;
}
