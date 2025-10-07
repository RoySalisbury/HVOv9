using HVO.SkyMonitorV5.RPi.Optics;

namespace HVO.SkyMonitorV5.RPi.Lenses;

/// <summary>
/// Fisheye lens defined by a projection model and an intended full diagonal FOV (deg).
/// </summary>
public sealed record FisheyeLens(ProjectionModel Model, double FovDeg) : ILens
{
    public double FocalPx(SensorSpec sensor, double imageRadiusPx)
    {
        var thetaMax = Math.Clamp(FovDeg, 1.0, 200.0) * Math.PI / 180.0 / 2.0;
        double g = Model switch
        {
            ProjectionModel.Equidistant    => thetaMax,
            ProjectionModel.EquisolidAngle => 2.0 * Math.Sin(thetaMax / 2.0),
            ProjectionModel.Orthographic   => Math.Sin(thetaMax),
            ProjectionModel.Stereographic  => 2.0 * Math.Tan(thetaMax / 2.0),
            ProjectionModel.Perspective    => Math.Tan(thetaMax),
            ProjectionModel.Gnomonic       => Math.Tan(thetaMax),
            _ => 1.0
        };
        if (g <= 1e-9) g = 1e-9;
        return imageRadiusPx / g; // ensure edge of FOV maps to circle edge
    }
}
