#nullable enable
namespace HVO.SkyMonitorV5.RPi.Optics;

/// <summary>Physical and raster specs of an imaging sensor.</summary>
public sealed record SensorSpec(
    int WidthPx,
    int HeightPx,
    double PixelSizeMicrons,   // e.g., 3.76 µm for IMX571/ASI2600
    double? PrincipalPointXPx = null, // null → center
    double? PrincipalPointYPx = null,
    double? Skew = null        // rarely used; keep 0 for most cameras
)
{
    public double PixelSizeMm => PixelSizeMicrons / 1000.0;
    public double Cx => PrincipalPointXPx ?? (WidthPx * 0.5);
    public double Cy => PrincipalPointYPx ?? (HeightPx * 0.5);
}
