#nullable enable
namespace HVO.SkyMonitorV5.RPi.Optics;

public enum ProjectionModel
{
    // Fisheye / ultra-wide (radial)
    Equidistant,     // r = f * θ
    EquisolidAngle,  // r = 2f * sin(θ/2)
    Orthographic,    // r = f * sin(θ)
    Stereographic,   // r = 2f * tan(θ/2)

    // Telescope / rectilinear (tangent plane)
    Perspective,     // r = f * tan(θ)
    Gnomonic,        // r = f * tan(θ)  (alias; charting-friendly)

    // Optional all-sky map projections
    Hammer,
    Mollweide
}
