#nullable enable

using System;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection;

public interface ICelestialProjector
{
    CelestialProjectionContext Create(CelestialProjectionSettings settings, DateTime utcUtc);
}
