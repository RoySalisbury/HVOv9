#nullable enable
using SkiaSharp;
using Microsoft.Extensions.Logging;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;

namespace HVO.SkyMonitorV5.RPi.Cameras.Rendering;

/// <summary>
/// Controls how star marker radii are computed from visual magnitude m (smaller m = brighter).
/// The radius is a smoothed logistic with an extra boost for extremely bright stars:
/// <code>
///   r(m) = RMinPx + (RMaxPx - RMinPx) / (1 + exp((m - MMid)/Slope)) + max(0, (-1 - m)) * BrightBoostPerMag
/// </code>
/// Parameters:
/// <list type="bullet">
/// <item><description><see cref="RMinPx"/> – minimum radius at the faint end (e.g., m ≥ 6.5).</description></item>
/// <item><description><see cref="RMaxPx"/> – radius approached by very bright stars before the bright boost.</description></item>
/// <item><description><see cref="MMid"/> – magnitude where the logistic is halfway between RMinPx and RMaxPx.
/// Increasing this shifts the curve toward fainter stars (more stars end up small).</description></item>
/// <item><description><see cref="Slope"/> – controls transition softness. Larger values → gentler slope (fewer big markers overall).</description></item>
/// <item><description><see cref="BrightBoostPerMag"/> – extra radius per mag for m &lt; -1 (Sirius, planets) to keep them visually prominent.</description></item>
/// </list>
/// </summary>
public sealed record StarSizeCurve(
    double RMinPx = 0.8,
    double RMaxPx = 2.9,
    double MMid   = 5.6,
    double Slope  = 1.40,
    double BrightBoostPerMag = 0.18
);

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
    private readonly StarSizeCurve _sizeCurve;
    private readonly ICelestialProjector? _projector;
    private readonly ILogger<StarFieldEngine>? _logger;

    public StarFieldEngine(
        int width,
        int height,
        double latitudeDeg,
        double longitudeDeg,
        DateTime utcUtc,
    ProjectionModel projectionModel = ProjectionModel.Equidistant,
        double horizonPaddingPct = 0.95,
        bool flipHorizontal = false,
        double fovDeg = 180.0,
        bool applyRefraction = false,
        ICelestialProjector? projector = null,
        ILogger<StarFieldEngine>? logger = null,
        StarSizeCurve? sizeCurve = null)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
    Projection = projectionModel;
        HorizonPaddingPct = horizonPaddingPct;

        _latitudeDeg = latitudeDeg;
        _longitudeDeg = longitudeDeg;
        _utc = utcUtc.ToUniversalTime();
        _flipHorizontal = flipHorizontal;
        _fovDeg = Math.Clamp(fovDeg, 120.0, 200.0);
        _applyRefraction = applyRefraction;
        _projector = projector;
        _logger = logger;
        _sizeCurve = sizeCurve ?? new StarSizeCurve();
    }

    public int Width { get; }
    public int Height { get; }
    public ProjectionModel Projection { get; }
    public double HorizonPaddingPct { get; }

    // Convenience overload
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

    // Full renderer
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
        // local helper: treat very small/faint stars as single-pixel dots
        static bool ShouldMicroDot(double mag, float radius) => (mag >= 5.2) || (radius < 1.05f);

        projectedStars = new List<StarProjection>(stars.Count + randomFillerCount);
        projectedPlanets = planets.Count == 0 ? new List<PlanetProjection>() : new List<PlanetProjection>(planets.Count);

        var bitmap = new SKBitmap(Width, Height, true);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var cx = Width * 0.5f;
        var cy = Height * 0.5f;
        var maxRadius = (float)(Math.Min(cx, cy) * HorizonPaddingPct);

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

                _logger?.LogTrace("StarFieldEngine Render start: {W}x{H}, FOV={FOV}, proj={Proj}", Width, Height, _fovDeg, Projection);
        var lstHours = LocalSiderealTime(_utc, _longitudeDeg);
        var latitudeRad = DegreesToRadians(_latitudeDeg);
        var scaleRatio = maxRadius / 480f; // radius scaling baseline

        // Stars
        for (var i = 0; i < stars.Count; i++)
        {
            var s = stars[i];
            if (!TryProject(s.RightAscensionHours, s.DeclinationDegrees, lstHours, latitudeRad,
                            cx, cy, maxRadius, Projection, _flipHorizontal, _applyRefraction, _fovDeg,
                            out var px, out var py))
                continue;

            var magnitude = s.Magnitude;
            var radius = RadiusFromMagnitude(magnitude, _sizeCurve, scaleRatio);

            var color = s.Color ?? SKColors.White;
            if (dimFaintStars)
            {
                var a = AlphaFromMagnitude(magnitude);
                color = new SKColor(color.Red, color.Green, color.Blue, a);
            }

            if (magnitude >= 6.0)
            {
                var pxI = (int)Math.Round(px);
                var pyI = (int)Math.Round(py);
                var oldAA = paint.IsAntialias;
                paint.IsAntialias = false;
                paint.Color = color;
                canvas.DrawRect(SKRect.Create(pxI, pyI, 2, 2), paint); // 2×2 for the very faintest
                paint.IsAntialias = oldAA;
            }
            else if (ShouldMicroDot(magnitude, radius))
            {
                var pxI = (int)Math.Round(px);
                var pyI = (int)Math.Round(py);
                var oldAA = paint.IsAntialias;
                paint.IsAntialias = false;
                paint.Color = color;
                canvas.DrawRect(SKRect.Create(pxI, pyI, 1, 1), paint); // 1×1 crisp pixel
                paint.IsAntialias = oldAA;
            }
            else
            {
                paint.Color = color;
                canvas.DrawCircle(px, py, Math.Max(1.0f, radius), paint);
            }

            projectedStars.Add(new StarProjection(i, px, py, magnitude));
        }

        // Optional random filler
        if (randomFillerCount > 0)
        {
            var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
            for (var i = 0; i < randomFillerCount; i++)
            {
                var raHours = rng.NextDouble() * 24.0;
                var sinDec = rng.NextDouble() * 2.0 - 1.0;
                var decDegrees = RadiansToDegrees(Math.Asin(Math.Clamp(sinDec, -1.0, 1.0)));
                var mag = rng.NextDouble() < 0.67 ? 5.0 + rng.NextDouble() * 2.0 : 3.0 + rng.NextDouble() * 2.0;

                if (!TryProject(raHours, decDegrees, lstHours, latitudeRad,
                                cx, cy, maxRadius, Projection, _flipHorizontal, _applyRefraction, _fovDeg,
                                out var px, out var py))
                    continue;

                var radius = RadiusFromMagnitude(mag, _sizeCurve, scaleRatio);
                var a = dimFaintStars ? AlphaFromMagnitude(mag) : (byte)210;
                var color = mag > 5.6
                    ? new SKColor(170, 170, 170, a)
                    : new SKColor(210, 210, 210, a);

                if (ShouldMicroDot(mag, radius))
                {
                    var pxI = (int)Math.Round(px);
                    var pyI = (int)Math.Round(py);
                    var oldAA = paint.IsAntialias;
                    paint.IsAntialias = false;
                    paint.Color = color;
                    canvas.DrawRect(SKRect.Create(pxI, pyI, 1, 1), paint);
                    paint.IsAntialias = oldAA;
                }
                else
                {
                    paint.Color = color;
                    canvas.DrawCircle(px, py, Math.Max(1.0f, radius), paint);
                }

                projectedStars.Add(new StarProjection(-1, px, py, mag));
            }
        }

        // Planets
        if (planets.Count > 0)
        {
            foreach (var p in planets)
            {
                if (!TryProject(p.Star.RightAscensionHours, p.Star.DeclinationDegrees,
                                lstHours, latitudeRad, cx, cy, maxRadius,
                                Projection, _flipHorizontal, _applyRefraction, _fovDeg,
                                out var px, out var py))
                    continue;

                var radius = p.Body == PlanetBody.Moon
                    ? planetOptions.MoonRadiusPx
                    : CalculatePlanetRadius(p.Star.Magnitude, planetOptions);

                radius = Math.Max(radius, 2.0f); // ensure visible

                DrawPlanetGlyph(canvas, px, py, radius, p, planetOptions.Shape, paint);
                projectedPlanets.Add(new PlanetProjection(p.Name, px, py, p.Star.Magnitude, p.Color));
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
        ProjectionModel projection,
        bool flipHorizontal,
        bool applyRefraction,
        double fovDeg,
        out float x,
        out float y)
    {
        x = 0f;
        y = 0f;

        // RA/Dec -> Alt/Az
        RaDecToAltAz(raHours, decDeg, lstHours, latitudeRad, out var altitudeDeg, out var azimuthDeg);

        // Apply refraction before horizon cull (apparent sky)
        if (applyRefraction)
        {
            altitudeDeg += BennettRefractionDeg(altitudeDeg);
        }

        // Cull if still below apparent horizon
        if (altitudeDeg < 0.0) return false;

        // Alt/Az -> projection plane
        if (!AltAzToProjection(altitudeDeg, azimuthDeg, cx, cy, maxRadius, projection, flipHorizontal, fovDeg, out x, out y))
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

    private static bool AltAzToProjection(double altitudeDeg, double azimuthDeg, float cx, float cy, float maxRadius, ProjectionModel projection, bool flipHorizontal, double fovDeg, out float x, out float y)
    {
        x = 0f; y = 0f;

        var theta = DegreesToRadians(90.0 - altitudeDeg); // zenith angle
        if (theta < 0) return false;

        var thetaMax = Math.PI * (fovDeg / 360.0); // FOV/2 in radians
        theta = Math.Min(theta, thetaMax);

        double rPrime = projection switch
        {
            ProjectionModel.Equidistant => theta / thetaMax,
            ProjectionModel.EquisolidAngle => Math.Sin(theta / 2.0) / Math.Sin(thetaMax / 2.0),
            ProjectionModel.Orthographic => Math.Sin(theta) / Math.Sin(thetaMax),
            ProjectionModel.Stereographic => Math.Tan(theta / 2.0) / Math.Tan(thetaMax / 2.0),

            // Rectilinear / central projections:
            ProjectionModel.Perspective => Math.Tan(theta) / Math.Tan(thetaMax),
            ProjectionModel.Gnomonic => Math.Tan(theta) / Math.Tan(thetaMax),

            _ => theta / thetaMax
        };

        rPrime = Math.Min(rPrime, 1.0);
        var radius = (float)(rPrime * maxRadius);
        var azimuthRad = DegreesToRadians(azimuthDeg);

        var horizontalOffset = (float)(radius * Math.Sin(azimuthRad));
        x = flipHorizontal ? cx - horizontalOffset : cx + horizontalOffset;
        y = cy - (float)(radius * Math.Cos(azimuthRad));
        return true;
    }

    // Bennett 1982 refraction (deg), reasonable near horizon.
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

    private static float RadiusFromMagnitude(double mag, StarSizeCurve c, double scaleRatio)
    {
        double r = c.RMinPx + (c.RMaxPx - c.RMinPx) / (1.0 + Math.Exp((mag - c.MMid) / c.Slope));
        if (mag < -1.0) r += (-1.0 - mag) * c.BrightBoostPerMag;
        r *= Math.Sqrt(Math.Max(0.25, scaleRatio));
        return (float)r;
    }

    private static byte AlphaFromMagnitude(double mag)
    {
        double a = mag switch
        {
            <= 1.0 => 255,
            <= 3.0 => 245 - 17.5 * (mag - 1.0),
            <= 5.0 => 210 - 17.5 * (mag - 3.0),
            _      => 175 - 10.0  * (mag - 5.0)
        };
        return (byte)Math.Clamp(a, 165, 255);
    }
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
