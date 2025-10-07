
#nullable enable
using HVO.SkyMonitorV5.RPi.Cameras.Optics;

namespace HVO.SkyMonitorV5.RPi.Cameras.Lenses
{
    /// <summary>
    /// Rectilinear/telescope lens defined by focal length in mm. Perspective model reserved
    /// until the rendering engine adds support. You can still use this to compute FOV.
    /// </summary>
    public sealed record RectilinearLens(
        double FocalLengthMm,
        ProjectionModel Model = ProjectionModel.Perspective,
        (double k1, double k2, double k3, double p1, double p2)? DistCoeffs = null
    ) : ILens
    {
        public (double k1, double k2, double k3, double p1, double p2)? Distortion => DistCoeffs;
        public double FocalPx(SensorSpec sensor, double imageRadiusPx)
        {
            return FocalLengthMm / sensor.PixelSizeMm;
        }
    }
}
