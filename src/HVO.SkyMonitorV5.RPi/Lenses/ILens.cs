using HVO.SkyMonitorV5.RPi.Optics;

namespace HVO.SkyMonitorV5.RPi.Lenses;

/// <summary>Lens model that maps θ↔r and provides focal length in pixels.</summary>
public interface ILens
{
    ProjectionModel Model { get; }

    /// <summary>
    /// Focal length in pixels for this lens on the given sensor.
    /// Implementations may derive from FOV or from focal length in mm.
    /// </summary>
    double FocalPx(SensorSpec sensor, double imageRadiusPx);

    /// <summary>Optional radial distortion (Brown–Conrady); null = none.</summary>
    (double k1, double k2, double k3, double p1, double p2)? Distortion => null;
}
