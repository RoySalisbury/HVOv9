using System;

namespace HVO.SkyMonitorV5.RPi.Infrastructure;

/// <summary>
/// Abstraction for invalidating cached configuration snapshots after write operations.
/// </summary>
public interface IConfigurationSnapshotInvalidator
{
    void InvalidateSnapshot();
}
