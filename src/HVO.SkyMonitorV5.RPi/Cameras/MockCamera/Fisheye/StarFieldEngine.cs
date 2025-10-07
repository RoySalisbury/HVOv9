#nullable enable
using HVO.Astronomy;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Cameras.MockCamera;

public enum FisheyeModel
{
    Equidistant,
    EquisolidAngle,
    Orthographic,
    Stereographic
}

public sealed record StarSizeCurve(
    double RMinPx = 0.8,
    double RMaxPx = 2.9,
    double MMid = 5.6,
    double Slope = 1.40,
    double BrightBoostPerMag = 0.18
);

public sealed record Star(double RightAscensionHours, double DeclinationDegrees, double Magnitude, SKColor? Color = null);

public readonly record struct StarProjection(int Index, float X, float Y, double Magnitude);
public readonly record struct PlanetProjection(string Name, float X, float Y, double Magnitude, SKColor Color);

public sealed class StarFieldEngine
{
    private readonly double _fovDeg;
    private readonly StarSizeCurve _sizeCurve;
    private readonly CelestialProjectionContext _projection;

    private const double MicroDotMagThreshold1x1 = 5.2;
    private const double MicroDotMagThreshold2x2 = 6.0;
    private const float MicroDotRadiusCutoff = 1.05f;

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
        bool applyRefraction = false,
        StarSizeCurve? sizeCurve = null,
        ICelestialProjector? projector = null)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        Projection = projection;
        HorizonPaddingPct = horizonPaddingPct;

        _fovDeg = Math.Clamp(fovDeg, 120.0, 200.0);
        _sizeCurve = sizeCurve ?? new StarSizeCurve();

        var settings = new CelestialProjectionSettings(
            width,
            height,
            latitudeDeg,
            longitudeDeg,
            projection,
            horizonPaddingPct,
            _fovDeg,
            applyRefraction,
            flipHorizontal);

        var effectiveProjector = projector ?? new CelestialProjector();
        _projection = effectiveProjector.Create(settings, utcUtc);
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

        var cx = _projection.CenterX;
        var cy = _projection.CenterY;
        var maxRadius = _projection.MaxRadius;

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        var scaleRatio = maxRadius / 480f;
        for (var i = 0; i < stars.Count; i++)
        {
            var s = stars[i];
            if (!_projection.TryProjectStar(s, out var px, out var py))
            {
                continue;
            }

            var magnitude = s.Magnitude;
            var radius = RadiusFromMagnitude(magnitude, _sizeCurve, scaleRatio);

            var color = s.Color ?? SKColors.White;
            if (dimFaintStars)
            {
                var a = AlphaFromMagnitude(magnitude);
                color = new SKColor(color.Red, color.Green, color.Blue, a);
            }

            var micro2x2 = magnitude >= MicroDotMagThreshold2x2;
            var micro1x1 = magnitude >= MicroDotMagThreshold1x1 || radius < MicroDotRadiusCutoff;

            if (micro2x2)
            {
                var pxI = (int)Math.Round(px);
                var pyI = (int)Math.Round(py);
                var oldAA = paint.IsAntialias;
                paint.IsAntialias = false;
                paint.Color = color;
                canvas.DrawRect(SKRect.Create(pxI, pyI, 2, 2), paint);
                paint.IsAntialias = oldAA;
            }
            else if (micro1x1)
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

            projectedStars.Add(new StarProjection(i, px, py, magnitude));
        }

        if (randomFillerCount > 0)
        {
            var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
            for (var i = 0; i < randomFillerCount; i++)
            {
                var raHours = rng.NextDouble() * 24.0;
                var sinDec = rng.NextDouble() * 2.0 - 1.0;
                var decDegrees = AstronomyMath.RadiansToDegrees(Math.Asin(Math.Clamp(sinDec, -1.0, 1.0)));
                var mag = rng.NextDouble() < 0.67 ? 5.0 + rng.NextDouble() * 2.0 : 3.0 + rng.NextDouble() * 2.0;

                if (!_projection.TryProjectEquatorial(raHours, decDegrees, out var px, out var py))
                {
                    continue;
                }

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

        if (planets.Count > 0)
        {
            foreach (var p in planets)
            {
                if (!_projection.TryProjectStar(p.Star, out var px, out var py))
                {
                    continue;
                }

                var radius = p.Body == PlanetBody.Moon
                    ? planetOptions.MoonRadiusPx
                    : CalculatePlanetRadius(p.Star.Magnitude, planetOptions);

                radius = Math.Max(radius, 2.0f);

                DrawPlanetGlyph(canvas, px, py, radius, p, planetOptions.Shape, paint);
                projectedPlanets.Add(new PlanetProjection(p.Name, px, py, p.Star.Magnitude, p.Color));
            }
        }

        return bitmap;
    }

    public bool ProjectStar(Star star, out float x, out float y)
        => _projection.TryProjectStar(star, out x, out y);

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

    private static float RadiusFromMagnitude(double mag, StarSizeCurve c, double scaleRatio)
    {
        double r = c.RMinPx + (c.RMaxPx - c.RMinPx) / (1.0 + Math.Exp((mag - c.MMid) / c.Slope));

        if (mag < -1.0)
        {
            r += (-1.0 - mag) * c.BrightBoostPerMag;
        }

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
            _ => 175 - 10.0 * (mag - 5.0)
        };
        return (byte)Math.Clamp(a, 165, 255);
    }

    private static bool ShouldMicroDot(double mag, float radius) =>
        mag >= 5.2 || radius < 1.05f;
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
            _ => SKColors.White
        };
    }

    public static SKColor FromBV(double bv)
    {
        bv = Math.Clamp(bv, -0.4, 2.0);
        double r, g, b;

        if (bv < 0.0) r = 0.61 + 0.11 * bv + 0.1 * bv * bv;
        else if (bv < 0.4) r = 0.83 + 0.17 * bv;
        else if (bv < 1.6) r = 1.00;
        else r = 1.00;

        if (bv < 0.0) g = 0.70 + 0.07 * bv + 0.1 * bv * bv;
        else if (bv < 0.4) g = 0.87 + 0.11 * bv;
        else if (bv < 1.5) g = 0.98 - 0.16 * (bv - 0.4);
        else g = 0.82 - 0.5 * (bv - 1.5);

        if (bv < 0.4) b = 1.00;
        else if (bv < 1.5) b = 1.00 - 0.47 * (bv - 0.4);
        else b = 0.63 - 0.6 * (bv - 1.5);

        byte R = (byte)Math.Clamp((int)Math.Round(r * 255), 0, 255);
        byte G = (byte)Math.Clamp((int)Math.Round(g * 255), 0, 255);
        byte B = (byte)Math.Clamp((int)Math.Round(b * 255), 0, 255);
        return new SKColor(R, G, B);
    }
}
