#nullable enable

using System;
using System.Collections.Generic;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Cameras.MockCamera;

/// <summary>
/// Provides styling metadata and helper routines for rendering planets.
/// </summary>
public static class PlanetTools
{
    private static readonly PlanetBody[] InnerPlanets =
    {
        PlanetBody.Mercury,
        PlanetBody.Venus,
        PlanetBody.Mars,
        PlanetBody.Jupiter,
        PlanetBody.Saturn
    };

    private static readonly IReadOnlyDictionary<PlanetBody, string> Names = new Dictionary<PlanetBody, string>
    {
        [PlanetBody.Mercury] = "Mercury",
        [PlanetBody.Venus] = "Venus",
        [PlanetBody.Mars] = "Mars",
        [PlanetBody.Jupiter] = "Jupiter",
        [PlanetBody.Saturn] = "Saturn",
        [PlanetBody.Uranus] = "Uranus",
        [PlanetBody.Neptune] = "Neptune",
        [PlanetBody.Moon] = "Moon",
        [PlanetBody.Sun] = "Sun"
    };

    public readonly record struct PlanetDisplayStyle(double BaseMagnitude, double Variation, SKColor Color);

    private static readonly IReadOnlyDictionary<PlanetBody, PlanetDisplayStyle> Styles = new Dictionary<PlanetBody, PlanetDisplayStyle>
    {
        [PlanetBody.Mercury] = new PlanetDisplayStyle(-0.6, 0.8, new SKColor(220, 220, 220)),
        [PlanetBody.Venus] = new PlanetDisplayStyle(-3.9, 0.3, new SKColor(245, 245, 210)),
        [PlanetBody.Mars] = new PlanetDisplayStyle(-1.3, 1.0, new SKColor(255, 120, 80)),
        [PlanetBody.Jupiter] = new PlanetDisplayStyle(-2.2, 0.3, new SKColor(255, 215, 0)),
        [PlanetBody.Saturn] = new PlanetDisplayStyle(-0.5, 0.3, new SKColor(245, 230, 170)),
        [PlanetBody.Uranus] = new PlanetDisplayStyle(5.7, 0.2, new SKColor(180, 220, 255)),
        [PlanetBody.Neptune] = new PlanetDisplayStyle(7.8, 0.2, new SKColor(170, 190, 255)),
        [PlanetBody.Moon] = new PlanetDisplayStyle(-12.0, 0.6, new SKColor(210, 210, 210)),
        [PlanetBody.Sun] = new PlanetDisplayStyle(-26.7, 0.0, new SKColor(255, 240, 200))
    };

    public static string GetDisplayName(PlanetBody body)
        => Names.TryGetValue(body, out var name) ? name : Enum.GetName(body) ?? body.ToString();

    public static bool TryGetDisplayStyle(PlanetBody body, out PlanetDisplayStyle style)
        => Styles.TryGetValue(body, out style);

    public static IReadOnlyList<PlanetBody> GetDefaultBodies(
        bool includePlanets,
        bool includeMoon,
        bool includeOuterPlanets,
        bool includeSun)
    {
        var bodies = new List<PlanetBody>();

        if (includePlanets)
        {
            bodies.AddRange(InnerPlanets);
        }

        if (includeOuterPlanets)
        {
            if (!bodies.Contains(PlanetBody.Uranus))
            {
                bodies.Add(PlanetBody.Uranus);
            }

            if (!bodies.Contains(PlanetBody.Neptune))
            {
                bodies.Add(PlanetBody.Neptune);
            }
        }

        if (includeMoon && !bodies.Contains(PlanetBody.Moon))
        {
            bodies.Add(PlanetBody.Moon);
        }

        if (includeSun && !bodies.Contains(PlanetBody.Sun))
        {
            bodies.Add(PlanetBody.Sun);
        }

        return bodies.Count == 0 ? Array.Empty<PlanetBody>() : bodies;
    }
}
