using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configuration values for the circular mask overlay.
/// </summary>
public sealed class CircularMaskOptions
{
    public const string SectionName = "CameraPipeline:CircularMask";

    [Range(-4096, 4096)]
    public float OffsetXPixels { get; set; }

    [Range(-4096, 4096)]
    public float OffsetYPixels { get; set; }

    [Range(-4096, 4096)]
    public float RadiusOffsetPixels { get; set; }

    [StringLength(16)]
    public string MaskColor { get; set; } = "#000000";

    [Range(0, 255)]
    public int MaskOpacity { get; set; } = 220;
}
