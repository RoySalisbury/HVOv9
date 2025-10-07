
namespace HVO.SkyMonitorV5.RPi.Cameras.Optics
{
    /// <summary>
    /// Unified projection model for lenses; current engine supports fisheye variants.
    /// Perspective/Gnomonic reserved for telescope mode in a later engine update.
    /// </summary>
    public enum ProjectionModel
    {
        Equidistant,
        EquisolidAngle,
        Orthographic,
        Stereographic,
        Perspective,   // reserved
        Gnomonic,      // reserved
        Hammer,        // reserved
        Mollweide      // reserved
    }
}
