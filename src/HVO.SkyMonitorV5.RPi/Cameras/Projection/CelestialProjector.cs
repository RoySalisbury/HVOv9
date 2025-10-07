#nullable enable

using System;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection;

public sealed class CelestialProjector : ICelestialProjector
{
    public CelestialProjectionContext Create(CelestialProjectionSettings settings, DateTime utcUtc)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return new CelestialProjectionContext(settings, utcUtc);
    }
}
