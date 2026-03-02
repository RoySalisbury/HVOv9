#nullable enable
using HVO.Core.Results;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras;

namespace HVO.SkyMonitorV5.RPi.Cameras.Drivers;

/// <summary>
/// Creates camera drivers that satisfy a specific <see cref="RigSpec"/>.
/// </summary>
public interface ICameraDriverFactory
{
    /// <summary>
    /// Creates a camera adapter instance for the supplied rig.
    /// </summary>
    /// <param name="rig">The rig definition describing the camera and optics.</param>
    /// <returns>A <see cref="Result{T}"/> containing the adapter or the failure reason.</returns>
    Result<ICameraAdapter> Create(RigSpec rig);
}
