using System.Text.Json.Serialization;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Describes the pixel layout for a raw frame payload exported to external storage.
/// </summary>
public sealed record FrameExportImageDescriptor(
    int Width,
    int Height,
    int RowBytes,
    int BytesPerPixel,
    string ColorType,
    string AlphaType,
    bool GammaIsLinear,
    bool IsSrgb,
    bool HasNumericalTransferFunction,
    string? ColorSpaceDescription)
{
    /// <summary>
    /// Gets a short, human-readable pixel format hint (e.g. "RgbaF16").
    /// </summary>
    [JsonIgnore]
    public string PixelFormatHint => ColorType;
}
