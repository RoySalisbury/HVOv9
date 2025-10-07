using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configuration for the cardinal directions overlay filter.
/// </summary>
public sealed class CardinalDirectionsOptions
{
    public const string SectionName = "CameraPipeline:CardinalDirections";

    [Range(-2048, 2048)]
    public float OffsetXPixels { get; set; }

    [Range(-2048, 2048)]
    public float OffsetYPixels { get; set; }

    [Range(-180, 180)]
    public float RotationDegrees { get; set; }

    [Range(-2048, 2048)]
    public float RadiusOffsetPixels { get; set; }

    [StringLength(32)]
    public string LabelNorth { get; set; } = "N";

    [StringLength(32)]
    public string LabelSouth { get; set; } = "S";

    [StringLength(32)]
    public string LabelEast { get; set; } = "E";

    [StringLength(32)]
    public string LabelWest { get; set; } = "W";

    public bool SwapEastWest { get; set; }

    [StringLength(16)]
    public string CircleColor { get; set; } = "#C8D2E6";

    [Range(0, 255)]
    public int CircleOpacity { get; set; } = 170;

    [Range(0.5, 10)]
    public float CircleThickness { get; set; } = 2f;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CardinalLineStyle CircleLineStyle { get; set; } = CardinalLineStyle.LongDash;

    [Range(0, 255)]
    public int LabelFillOpacity { get; set; } = 160;

    [Range(0, 32)]
    public float LabelPadding { get; set; } = 6f;

    [Range(0, 32)]
    public float LabelCornerRadius { get; set; } = 6f;

    [Range(8, 72)]
    public float LabelFontSize { get; set; } = 22f;
}

public enum CardinalLineStyle
{
    Solid,
    LongDash,
    ShortDash,
    Dotted,
    DashDot
}
