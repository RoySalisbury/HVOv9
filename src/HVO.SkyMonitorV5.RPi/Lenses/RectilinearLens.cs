using HVO.SkyMonitorV5.RPi.Optics;

namespace HVO.SkyMonitorV5.RPi.Lenses;

/// <summary>
/// Telescope/rectilinear lens by focal length in mm; projection is Perspective/Gnomonic.
/// Pixel focal = f_mm / pixel_size_mm.
/// </summary>
public sealed record RectilinearLens(double FocalLengthMm, ProjectionModel Model = ProjectionModel.Perspective) : ILens
{
    public ProjectionModel Model { get; init; } = Model;

    public double FocalPx(SensorSpec sensor, double imageRadiusPx)
    {
        // classic pixel-scale relation: f_px = f_mm / pixel_size_mm
        return FocalLengthMm / sensor.PixelSizeMm;
    }
}
