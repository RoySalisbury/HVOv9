#nullable enable
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Cameras.MockCamera;

public enum FisheyeModel
{
    Equidistant,
    EquisolidAngle,
    Orthographic,
    Stereographic
}

public sealed record Star(double RightAscensionHours, double DeclinationDegrees, double Magnitude, SKColor? Color = null);

public readonly record struct StarProjection(int Index, float X, float Y, double Magnitude);
public readonly record struct PlanetProjection(string Name, float X, float Y, double Magnitude, SKColor Color);

public sealed class StarFieldEngine
{
    private readonly double _latitudeDeg;
    private readonly double _longitudeDeg;
    private readonly DateTime _utc;
    private readonly bool _flipHorizontal;
    private readonly double _fovDeg;
    private readonly bool _applyRefraction;

    public StarFieldEngine(
        int width,
        int height,
        double latitudeDeg,
        double longitudeDeg,
        DateTime utcUtc,
        FisheyeModel projection = FisheyeModel.Equidistant,
        double horizonPaddingPct = 0.95,
        bool flipHorizontal = false,
        double fovDeg = 180.0,
        bool applyRefraction = false)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        Projection = projection;
        HorizonPaddingPct = horizonPaddingPct;

        _latitudeDeg = latitudeDeg;
        _longitudeDeg = longitudeDeg;
        _utc = utcUtc.ToUniversalTime();
        _flipHorizontal = flipHorizontal;
        _fovDeg = Math.Clamp(fovDeg, 120.0, 200.0);
        _applyRefraction = applyRefraction;
    }

    public int Width { get; }
    public int Height { get; }
    public FisheyeModel Projection { get; }
    public double HorizonPaddingPct { get; }

    public SKBitmap Render(
        IReadOnlyList<Star> stars,
        int randomFillerCount,
        int? randomSeed,
        bool dimFaintStars,
        out List<StarProjection> projectedStars)
    {
        return Render(
            stars,
            Array.Empty<PlanetMark>(),
            randomFillerCount,
            randomSeed,
            dimFaintStars,
            PlanetRenderOptions.Default,
            out projectedStars,
            out _);
    }

    public SKBitmap Render(
        IReadOnlyList<Star> stars,
        IReadOnlyList<PlanetMark> planets,
        int randomFillerCount,
        int? randomSeed,
        bool dimFaintStars,
        PlanetRenderOptions planetOptions,
        out List<StarProjection> projectedStars,
        out List<PlanetProjection> projectedPlanets)
    {
        projectedStars = new List<StarProjection>(stars.Count + randomFillerCount);
        projectedPlanets = planets.Count == 0 ? new List<PlanetProjection>() : new List<PlanetProjection>(planets.Count);

        var bitmap = new SKBitmap(Width, Height, true);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var cx = Width * 0.5f;
        var cy = Height * 0.5f;
        var maxRadius = (float)(Math.Min(cx, cy) * HorizonPaddingPct);

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        var lstHours = LocalSiderealTime(_utc, _longitudeDeg);
        var latitudeRad = DegreesToRadians(_latitudeDeg);

        // Stars
        for (var i = 0; i < stars.Count; i++)
        {
            var star = stars[i];
            if (!TryProject(star.RightAscensionHours, star.DeclinationDegrees, lstHours, latitudeRad,
                            cx, cy, maxRadius, Projection, _flipHorizontal, _applyRefraction, _fovDeg,
                            out var px, out var py))
                continue;

            var magnitude = star.Magnitude;
            var brightness = Math.Pow(10.0, -(magnitude + 1.0) / 2.5);
            var radius = (float)(Math.Clamp(brightness, 0.001, 1.0) * 4.0 + 0.6f);

            var color = star.Color ?? (dimFaintStars && magnitude > 5.5 ? new SKColor(170, 170, 170) : SKColors.White);

            paint.Color = color;
            canvas.DrawCircle(px, py, radius, paint);
            projectedStars.Add(new StarProjection(i, px, py, magnitude));
        }

        // Random filler
        if (randomFillerCount > 0)
        {
            var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
            for (var i = 0; i < randomFillerCount; i++)
            {
                var raHours = rng.NextDouble() * 24.0;
                var sinDec = rng.NextDouble() * 2.0 - 1.0;
                var decDegrees = RadiansToDegrees(Math.Asin(Math.Clamp(sinDec, -1.0, 1.0)));
                var magnitude = rng.NextDouble() < 0.67 ? 5.0 + rng.NextDouble() * 2.0 : 3.0 + rng.NextDouble() * 2.0;

                if (!TryProject(raHours, decDegrees, lstHours, latitudeRad,
                                cx, cy, maxRadius, Projection, _flipHorizontal, _applyRefraction, _fovDeg,
                                out var px, out var py))
                    continue;

                var fillerBrightness = Math.Pow(10.0, -(magnitude + 1.0) / 2.5);
                var fillerRadius = (float)(Math.Clamp(fillerBrightness, 0.001, 1.0) * 3.4 + 0.5f);
                var fillerColor = magnitude > 5.6 ? new SKColor(150, 150, 150) : new SKColor(210, 210, 210);

                paint.Color = fillerColor;
                canvas.DrawCircle(px, py, fillerRadius, paint);
                projectedStars.Add(new StarProjection(-1, px, py, magnitude));
            }
        }

        // Planets
        if (planets.Count > 0)
        {
            foreach (var planet in planets)
            {
                var ok = TryProject(
                    planet.Star.RightAscensionHours, planet.Star.DeclinationDegrees,
                    lstHours, latitudeRad, cx, cy, maxRadius,
                    Projection, _flipHorizontal, _applyRefraction, _fovDeg,
                    out var px, out var py);

                if (planet.Name == "Jupiter")
                    Console.WriteLine($"Jupiter TryProject={ok} px={px} py={py}");

                if (!ok) continue;

                var radius = planet.Body == PlanetBody.Moon
                    ? planetOptions.MoonRadiusPx
                    : CalculatePlanetRadius(planet.Star.Magnitude, planetOptions);

                // safety: ensure visible
                radius = Math.Max(radius, 2.0f);

                DrawPlanetGlyph(canvas, px, py, radius, planet, planetOptions.Shape, paint);
                projectedPlanets.Add(new PlanetProjection(planet.Name, px, py, planet.Star.Magnitude, planet.Color));
            }
        }

        return bitmap;
    }

    public bool ProjectStar(Star star, out float x, out float y)
    {
        var lstHours = LocalSiderealTime(_utc, _longitudeDeg);
        var latitudeRad = DegreesToRadians(_latitudeDeg);
        var cx = Width * 0.5f;
        var cy = Height * 0.5f;
        var maxRadius = (float)(Math.Min(cx, cy) * HorizonPaddingPct);

        return TryProject(star.RightAscensionHours, star.DeclinationDegrees, lstHours, latitudeRad,
                          cx, cy, maxRadius, Projection, _flipHorizontal, _applyRefraction, _fovDeg,
                          out x, out y);
    }

    private static float CalculatePlanetRadius(double magnitude, PlanetRenderOptions options)
    {
        var brightness = Math.Pow(10.0, -(magnitude + 1.0) / 2.5);
        var unclamped = (float)(Math.Clamp(brightness, 0.001, 1.0) * 6.0 + 1.2f);
        return Math.Clamp(unclamped, options.MinRadiusPx, options.MaxRadiusPx);
    }

private static void DrawPlanetGlyph(SKCanvas canvas, float x, float y, float radius, PlanetMark planet, PlanetMarkerShape shape, SKPaint paint)
    {
        paint.Color = planet.Color;
        switch (shape)
        {
            case PlanetMarkerShape.Square:
                canvas.DrawRect(x - radius, y - radius, radius * 2, radius * 2, paint);
                break;
            case PlanetMarkerShape.Diamond:
                using (var path = new SKPath())
                {
                    path.MoveTo(x, y - radius);
                    path.LineTo(x + radius, y);
                    path.LineTo(x, y + radius);
                    path.LineTo(x - radius, y);
                    path.Close();
                    canvas.DrawPath(path, paint);
                }
                break;
            default:
                canvas.DrawCircle(x, y, radius, paint);
                break;
        }
    }

    private static bool TryProject(
        double raHours,
        double decDeg,
        double lstHours,
        double latitudeRad,
        float cx,
        float cy,
        float maxRadius,
        FisheyeModel projection,
        bool flipHorizontal,
        bool applyRefraction,
        double fovDeg,
        out float x,
        out float y)
    {
        x = 0f;
        y = 0f;

        // RA/Dec -> Alt/Az (always succeed; cull later)
        RaDecToAltAz(raHours, decDeg, lstHours, latitudeRad, out var altitudeDeg, out var azimuthDeg);

        // Apply refraction before horizon cull (apparent sky)
        if (applyRefraction)
        {
            altitudeDeg += BennettRefractionDeg(altitudeDeg);
        }

        // Now cull if still below the apparent horizon
        if (altitudeDeg < 0.0) return false;

        // Alt/Az -> fisheye
        if (!AltAzToFisheye(altitudeDeg, azimuthDeg, cx, cy, maxRadius, projection, flipHorizontal, fovDeg, out x, out y))
            return false;

        var dx = x - cx;
        var dy = y - cy;
        return dx * dx + dy * dy <= maxRadius * maxRadius + 1.0f;
    }

    private static void RaDecToAltAz(double raHours, double decDeg, double lstHours, double latitudeRad, out double altitudeDeg, out double azimuthDeg)
    {
        var hourAngle = DegreesToRadians((lstHours - raHours) * 15.0);
        var declinationRad = DegreesToRadians(decDeg);
        var sinAlt = Math.Sin(declinationRad) * Math.Sin(latitudeRad) + Math.Cos(declinationRad) * Math.Cos(latitudeRad) * Math.Cos(hourAngle);
        var altitude = Math.Asin(Math.Clamp(sinAlt, -1.0, 1.0));

        var cosAz = (Math.Sin(declinationRad) - Math.Sin(altitude) * Math.Sin(latitudeRad)) / (Math.Cos(altitude) * Math.Cos(latitudeRad));
        cosAz = Math.Clamp(cosAz, -1.0, 1.0);

        var azimuth = Math.Acos(cosAz);
        if (Math.Sin(hourAngle) > 0) azimuth = Math.PI * 2.0 - azimuth;

        altitudeDeg = RadiansToDegrees(altitude);
        azimuthDeg = RadiansToDegrees(azimuth);
    }

    private static bool AltAzToFisheye(double altitudeDeg, double azimuthDeg, float cx, float cy, float maxRadius, FisheyeModel projection, bool flipHorizontal, double fovDeg, out float x, out float y)
    {
        x = 0f; y = 0f;

        var theta = DegreesToRadians(90.0 - altitudeDeg); // zenith angle
        if (theta < 0) return false;

        var thetaMax = Math.PI * (fovDeg / 360.0); // FOV/2 in radians
        theta = Math.Min(theta, thetaMax);

        double rPrime = projection switch
        {
            FisheyeModel.Equidistant      => theta / thetaMax,
            FisheyeModel.EquisolidAngle   => Math.Sin(theta / 2.0) / Math.Sin(thetaMax / 2.0),
            FisheyeModel.Orthographic     => Math.Sin(theta) / Math.Sin(thetaMax),
            FisheyeModel.Stereographic    => Math.Tan(theta / 2.0) / Math.Tan(thetaMax / 2.0),
            _                             => theta / thetaMax
        };

        rPrime = Math.Min(rPrime, 1.0);
        var radius = (float)(rPrime * maxRadius);
        var azimuthRad = DegreesToRadians(azimuthDeg);

        var horizontalOffset = (float)(radius * Math.Sin(azimuthRad));
        x = flipHorizontal ? cx - horizontalOffset : cx + horizontalOffset;
        y = cy - (float)(radius * Math.Cos(azimuthRad));
        return true;
    }

    // Bennett 1982 refraction (deg). Good near horizon.
    private static double BennettRefractionDeg(double altDeg)
    {
        var a = Math.Max(altDeg, -0.9);
        var rArcMin = 1.02 / Math.Tan((a + 10.3 / (a + 5.11)) * Math.PI / 180.0);
        return rArcMin / 60.0;
    }

    public static double LocalSiderealTime(DateTime utc, double longitudeDeg)
    {
        var jd = OADateToJulian(utc);
        var t = (jd - 2451545.0) / 36525.0;
        var gmst = 6.697374558 + 2400.051336 * t + 0.000025862 * t * t;
        var fractionalDay = (jd + 0.5) % 1.0;
        gmst = (gmst + fractionalDay * 24.0 * 1.00273790935) % 24.0;
        var lst = (gmst + longitudeDeg / 15.0) % 24.0;
        if (lst < 0) lst += 24.0;
        return lst;
    }

    private static double OADateToJulian(DateTime utc) => utc.ToOADate() + 2415018.5;
    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}

public static class StarColors
{
    public static SKColor FromCatalog(string? spectral, double? colorIndexBV)
        => (colorIndexBV is double bv && !double.IsNaN(bv)) ? FromBV(bv) : SpectralColor(spectral);

    public static SKColor SpectralColor(string? spectral)
    {
        if (string.IsNullOrWhiteSpace(spectral)) return SKColors.White;
        char c = char.ToUpperInvariant(spectral.Trim()[0]);
        return c switch
        {
            'O' => new SKColor(155, 176, 255),
            'B' => new SKColor(170, 191, 255),
            'A' => new SKColor(202, 215, 255),
            'F' => new SKColor(248, 247, 255),
            'G' => new SKColor(255, 244, 234),
            'K' => new SKColor(255, 210, 161),
            'M' => new SKColor(255, 180, 140),
            _   => SKColors.White
        };
    }

    public static SKColor FromBV(double bv)
    {
        bv = Math.Clamp(bv, -0.4, 2.0);
        double r, g, b;

        if (bv < 0.0)       r = 0.61 + 0.11 * bv + 0.1 * bv * bv;
        else if (bv < 0.4)  r = 0.83 + 0.17 * bv;
        else if (bv < 1.6)  r = 1.00;
        else                r = 1.00;

        if (bv < 0.0)       g = 0.70 + 0.07 * bv + 0.1 * bv * bv;
        else if (bv < 0.4)  g = 0.87 + 0.11 * bv;
        else if (bv < 1.5)  g = 0.98 - 0.16 * (bv - 0.4);
        else                g = 0.82 - 0.5  * (bv - 1.5);

        if (bv < 0.4)       b = 1.00;
        else if (bv < 1.5)  b = 1.00 - 0.47 * (bv - 0.4);
        else                b = 0.63 - 0.6  * (bv - 1.5);

        byte R = (byte)Math.Clamp((int)Math.Round(r * 255), 0, 255);
        byte G = (byte)Math.Clamp((int)Math.Round(g * 255), 0, 255);
        byte B = (byte)Math.Clamp((int)Math.Round(b * 255), 0, 255);
        return new SKColor(R, G, B);
    }
}
