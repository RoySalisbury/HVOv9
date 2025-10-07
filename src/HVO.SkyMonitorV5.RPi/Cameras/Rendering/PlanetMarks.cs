#nullable enable

using System;
using System.Collections.Generic;
using HVO;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Cameras.Rendering;

public enum PlanetMarkerShape { Circle, Square, Diamond }

public enum PlanetBody
{
    Mercury,
    Venus,
    Mars,
    Jupiter,
    Saturn,
    Uranus,
    Neptune,
    Moon,
    Sun
}

public sealed record PlanetMark(string Name, PlanetBody Body, Star Star, SKColor Color);

public sealed record PlanetRenderOptions(
    float MinRadiusPx = 3.5f,
    float MaxRadiusPx = 9.5f,
    float MoonRadiusPx = 12.0f,
    PlanetMarkerShape Shape = PlanetMarkerShape.Circle)
{
    public static PlanetRenderOptions Default { get; } = new();
}

public static class PlanetMarks
{
    public static IReadOnlyList<PlanetMark> Compute(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        bool includeUranusNeptune = false,
        bool includeSun = false)
    {
        var bodies = PlanetTools.GetDefaultBodies(
            includePlanets: true,
            includeMoon: true,
            includeOuterPlanets: includeUranusNeptune,
            includeSun: includeSun);

        return Compute(latitudeDeg, longitudeDeg, utc, bodies);
    }

    public static IReadOnlyList<PlanetMark> Compute(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        IReadOnlyCollection<PlanetBody> bodies)
    {
        if (bodies is null)
        {
            throw new ArgumentNullException(nameof(bodies));
        }

        if (bodies.Count == 0)
        {
            return Array.Empty<PlanetMark>();
        }

        var positions = PlanetMath.ComputeTopocentricPositions(latitudeDeg, longitudeDeg, utc, bodies);
        var marks = new List<PlanetMark>(positions.Count);

        foreach (var position in positions)
        {
            if (!PlanetTools.TryGetDisplayStyle(position.Body, out var style))
            {
                continue;
            }

            var star = new Star(position.RightAscensionHours, position.DeclinationDegrees, style.BaseMagnitude, style.Color);
            var name = PlanetTools.GetDisplayName(position.Body);
            marks.Add(new PlanetMark(name, position.Body, star, style.Color));
        }

        return marks;
    }
}
