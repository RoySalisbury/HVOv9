using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Cameras.Drivers;

/// <summary>
/// Provides discovery and lookup services for camera driver implementations.
/// </summary>
public interface ICameraDriverRegistry
{
    IReadOnlyCollection<CameraDriverDescriptor> GetDrivers();

    bool TryGetDriver(string id, out CameraDriverDescriptor descriptor);
}
