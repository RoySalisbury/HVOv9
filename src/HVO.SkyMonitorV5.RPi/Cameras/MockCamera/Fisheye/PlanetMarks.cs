using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Cameras.MockCamera;

public enum PlanetMarkerShape
{
    Circle,
    Square,
    Diamond
}

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
    private const double ObliquityDegrees = 23.43928;

    private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

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

    private static readonly IReadOnlyDictionary<PlanetBody, PlanetEphemeris> Elements = new Dictionary<PlanetBody, PlanetEphemeris>
    {
    [PlanetBody.Mercury] = new(252.250906, 4.09233445, 20.0, 7.0, 1.0, 110.0, -0.6, 0.8, new SKColor(220, 220, 220)),
    [PlanetBody.Venus] = new(181.979801, 1.60213034, 24.0, 3.4, 0.8, 70.0, -3.9, 0.3, new SKColor(245, 245, 210)),
    [PlanetBody.Mars] = new(355.433000, 0.52407100, 28.0, 4.0, 0.6, 15.0, -1.3, 1.0, new SKColor(255, 120, 80)),
    [PlanetBody.Jupiter] = new(34.351519, 0.08309100, 30.0, 1.5, 0.4, 45.0, -2.2, 0.3, new SKColor(255, 215, 0)),
    [PlanetBody.Saturn] = new(50.077444, 0.03345970, 32.0, 1.3, 0.3, 10.0, -0.5, 0.3, new SKColor(245, 230, 170)),
    [PlanetBody.Uranus] = new(314.055005, 0.01173129, 40.0, 1.0, 0.2, 25.0, 5.7, 0.2, new SKColor(180, 220, 255)),
    [PlanetBody.Neptune] = new(304.348665, 0.00598103, 45.0, 1.0, 0.2, 80.0, 7.8, 0.2, new SKColor(170, 190, 255)),
    [PlanetBody.Moon] = new(218.3164477, 13.17639648, 28.0, 5.1, 3.2, 125.0, -12.0, 0.6, new SKColor(210, 210, 210)),
    [PlanetBody.Sun] = new(280.46646, 0.98564736, 0.0, 0.0, 0.0, 0.0, -26.7, 0.0, new SKColor(255, 240, 200))
    };

    public static List<PlanetMark> Compute(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        bool includeUranusNeptune = false,
        bool includeSun = false)
    {
        _ = latitudeDeg;
        _ = longitudeDeg;

        var days = (utc.ToUniversalTime() - J2000).TotalDays;

        var bodies = new List<PlanetBody>
        {
            PlanetBody.Mercury,
            PlanetBody.Venus,
            PlanetBody.Mars,
            PlanetBody.Jupiter,
            PlanetBody.Saturn,
            PlanetBody.Moon
        };

        if (includeUranusNeptune)
        {
            bodies.Add(PlanetBody.Uranus);
            bodies.Add(PlanetBody.Neptune);
        }

        if (includeSun)
        {
            bodies.Add(PlanetBody.Sun);
        }

        var marks = new List<PlanetMark>(bodies.Count);

        foreach (var body in bodies)
        {
            var ephemeris = Elements[body];
            var lonDeg = NormalizeDegrees(ephemeris.LongitudeAtEpoch + (ephemeris.LongitudeRateDegPerDay * days));
            lonDeg += ephemeris.LongitudeWobbleAmplitudeDeg * Math.Sin(DegreesToRadians(days * 0.1 + body.GetHashCode()));

            var latDeg = ephemeris.LatitudeAmplitudeDeg == 0
                ? 0.0
                : ephemeris.LatitudeAmplitudeDeg * Math.Sin(DegreesToRadians(days * ephemeris.LatitudeFrequency + ephemeris.LatitudePhaseDeg));

            var (raHours, decDegrees) = ToEquatorial(lonDeg, latDeg);
            var magnitude = ephemeris.BaseMagnitude + ephemeris.MagnitudeVariation * Math.Cos(DegreesToRadians(lonDeg));
            var color = ephemeris.Color;

            var star = new Star(raHours, decDegrees, magnitude, color);
            marks.Add(new PlanetMark(Names[body], body, star, color));
        }

        return marks;
    }

    private static (double RaHours, double DecDegrees) ToEquatorial(double eclipticLongitudeDeg, double eclipticLatitudeDeg)
    {
        var lonRad = DegreesToRadians(eclipticLongitudeDeg);
        var latRad = DegreesToRadians(eclipticLatitudeDeg);
        var obliquityRad = DegreesToRadians(ObliquityDegrees);

        var sinLon = Math.Sin(lonRad);
        var cosLon = Math.Cos(lonRad);
        var sinLat = Math.Sin(latRad);
        var cosLat = Math.Cos(latRad);

        var x = cosLat * cosLon;
        var y = cosLat * sinLon * Math.Cos(obliquityRad) - sinLat * Math.Sin(obliquityRad);
        var z = cosLat * sinLon * Math.Sin(obliquityRad) + sinLat * Math.Cos(obliquityRad);

        var ra = Math.Atan2(y, x);
        if (ra < 0)
        {
            ra += Math.Tau;
        }

        var dec = Math.Asin(Math.Clamp(z, -1.0, 1.0));
        return (ra * 12.0 / Math.PI, dec * 180.0 / Math.PI);
    }

    private static double NormalizeDegrees(double value)
    {
        var normalized = value % 360.0;
        if (normalized < 0)
        {
            normalized += 360.0;
        }

        return normalized;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private sealed record PlanetEphemeris(
        double LongitudeAtEpoch,
        double LongitudeRateDegPerDay,
        double LongitudeWobbleAmplitudeDeg,
        double LatitudeAmplitudeDeg,
        double LatitudeFrequency,
        double LatitudePhaseDeg,
        double BaseMagnitude,
        double MagnitudeVariation,
        SKColor Color);
}
