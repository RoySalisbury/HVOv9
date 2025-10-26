
#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Cameras.Lenses
{
    /// <summary>Fisheye lens defined by model and diagonal FOV in degrees.</summary>
    public sealed record FisheyeLens(
        ProjectionModel Model,
        double FovDeg,
        (double k1, double k2, double k3, double p1, double p2)? DistCoeffs = null
    ) : ILens
    {
        public (double k1, double k2, double k3, double p1, double p2)? Distortion => DistCoeffs;

        public double FocalPx(SensorSpec sensor, double imageRadiusPx)
        {
            // For now, the engine computes per CelestialProjectionSettings, so we only carry metadata.
            // If needed, diagonal FOV can be converted to f_px = r_max / g(theta_max) in a future update.
            var thetaMax = Math.Clamp(FovDeg, 1.0, 200.0) * Math.PI / 180.0 * 0.5;
            double g = Model switch
            {
                ProjectionModel.Equidistant    => thetaMax,
                ProjectionModel.EquisolidAngle => 2.0 * Math.Sin(thetaMax / 2.0),
                ProjectionModel.Orthographic   => Math.Sin(thetaMax),
                ProjectionModel.Stereographic  => 2.0 * Math.Tan(thetaMax / 2.0),
                _ => thetaMax
            };
            if (g < 1e-9) g = 1e-9;
            return imageRadiusPx / g;
        }
    }
}
