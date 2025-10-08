
#nullable enable
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Cameras.Lenses
{
    /// <summary>Lens model abstraction. Returns pixel focal length for a given sensor.</summary>
    public interface ILens
    {
        ProjectionModel Model { get; }
        double FocalPx(SensorSpec sensor, double imageRadiusPx);
        (double k1, double k2, double k3, double p1, double p2)? Distortion { get; }
    }
}
