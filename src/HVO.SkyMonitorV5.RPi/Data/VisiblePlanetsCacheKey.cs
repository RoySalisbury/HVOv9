#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Data;

public readonly record struct VisiblePlanetsCacheKey(
    double LatitudeDeg,
    double LongitudeDeg,
    DateTime UtcHour,
    bool IncludePlanets,
    bool IncludeMoon,
    bool IncludeOuterPlanets,
    bool IncludeSun,
    string AllowedBodiesToken)
{
    public static string CreateAllowedBodiesToken(IReadOnlyCollection<PlanetBody>? allowedBodies)
    {
        if (allowedBodies is not { Count: > 0 })
        {
            return string.Empty;
        }

        var identifiers = allowedBodies
            .Select(body => ((int)body).ToString())
            .OrderBy(value => value, StringComparer.Ordinal);

        return string.Join(',', identifiers);
    }
}
