#nullable enable
namespace HVO.SkyMonitorV5.RPi.Cameras.Optics;

public interface IRigProjector
{
    /// <summary>
    /// Projects a sky direction (Alt/Az, degrees) into pixel coordinates for the
    /// configured camera+lens rig. Returns false when the ray falls outside the sensor.
    /// </summary>
    bool TryProjectAltAz(double altDeg, double azDeg, out float x, out float y);
}
