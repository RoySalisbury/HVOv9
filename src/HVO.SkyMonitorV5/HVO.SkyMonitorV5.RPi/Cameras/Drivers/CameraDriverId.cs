#nullable enable
namespace HVO.SkyMonitorV5.RPi.Cameras.Drivers;

/// <summary>
/// Identifies the registered camera driver implementation that can service a <see cref="CameraSpec"/>.
/// </summary>
public enum CameraDriverId
{
    Unknown = 0,
    /// <summary>
    /// Camera is serviced by a synthetic/mock driver implemented within the adapter.
    /// </summary>
    Synthetic,
    /// <summary>
    /// Camera uses the ZWO native driver stack.
    /// </summary>
    Zwo
}
