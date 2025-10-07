#nullable enable
using System;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection
{
    /// <summary>
    /// DI-friendly projector service. Implements <see cref="ICelestialProjector"/> by
    /// constructing a <see cref="CelestialProjectionContext"/> from settings + UTC.
    /// </summary>
    public sealed class CelestialProjector : ICelestialProjector
    {
        public CelestialProjectionContext Create(CelestialProjectionSettings settings, DateTime utcUtc)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            return new CelestialProjectionContext(settings, utcUtc);
        }
    }
}
