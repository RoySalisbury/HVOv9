#nullable enable
using System;
using SkiaSharp;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;

namespace HVO.SkyMonitorV5.RPi.Cameras.Rendering
{
    public enum ProjectionModel
    {
        Equidistant,
        EquisolidAngle,
        Orthographic,
        Stereographic,
        Perspective,
        Gnomonic
    }

    /// <summary>
    /// Visual size curve for point stars as a function of magnitude.
    /// RMin/RMax are radii in pixels for faint vs. bright stars (before bright boost);
    /// MMid is the magnitude at the curve’s midpoint; Slope controls the transition softness;
    /// BrightBoostPerMag adds a small linear boost for extremely bright stars (mag &lt; -1).
    /// </summary>
    public sealed record StarSizeCurve(
        double RMinPx = 0.8,
        double RMaxPx = 2.9,
        double MMid = 5.6,
        double Slope = 1.40,
        double BrightBoostPerMag = 0.18
    );

    public sealed record Star(
        double RightAscensionHours,
        double DeclinationDegrees,
        double Magnitude,
        SKColor? Color = null,
        string? CommonName = null,
        string? Designation = null,
        int? HarvardRevisedNumber = null);
    public readonly record struct StarProjection(int Index, float X, float Y, double Magnitude);
    public readonly record struct PlanetProjection(string Name, float X, float Y, double Magnitude, SKColor Color);

    /// <summary>
    /// Lens-agnostic sky renderer. It relies on an <see cref="IImageProjector"/> to map camera-space rays to pixels.
    /// </summary>
    public sealed class StarFieldEngine : IDisposable
    {
    private readonly IImageProjector _projector;
        private readonly double _latitudeDeg;
        private readonly double _longitudeDeg;
        private readonly DateTime _utc;
        private readonly bool _flipHorizontal;
        private readonly bool _applyRefraction;
        private readonly StarSizeCurve _sizeCurve;
        private bool _disposed;

        /// <summary>
        /// Preferred constructor: pass a ready projector (fisheye, rectilinear, etc).
        /// </summary>
        public StarFieldEngine(
            IImageProjector projector,
            double latitudeDeg,
            double longitudeDeg,
            DateTime utcUtc,
            bool flipHorizontal = false,
            bool applyRefraction = false,
            StarSizeCurve? sizeCurve = null)
        {
            _projector = projector ?? throw new ArgumentNullException(nameof(projector));
            _latitudeDeg = latitudeDeg;
            _longitudeDeg = longitudeDeg;
            _utc = utcUtc.ToUniversalTime();
            _flipHorizontal = flipHorizontal;
            _applyRefraction = applyRefraction;
            _sizeCurve = sizeCurve ?? new StarSizeCurve();
        }

        public StarFieldEngine(
            RigSpec rig,
            double latitudeDeg,
            double longitudeDeg,
            DateTime utcUtc,
            bool flipHorizontal = false,
            bool applyRefraction = false,
            double horizonPadding = 0.98,
            double? overrideCx = null,
            double? overrideCy = null,
            StarSizeCurve? sizeCurve = null)
            : this(
                RigFactory.CreateProjector(rig ?? throw new ArgumentNullException(nameof(rig)), horizonPadding, overrideCx, overrideCy),
                latitudeDeg,
                longitudeDeg,
                utcUtc,
                flipHorizontal,
                applyRefraction,
                sizeCurve)
        {
        }

        /// <summary>
        /// Compatibility constructor for legacy call sites that passed width/height/FOV/projection.
        /// Internally constructs a fisheye projector with the requested model/FOV.
        /// </summary>
        public StarFieldEngine(
            int width,
            int height,
            double latitudeDeg,
            double longitudeDeg,
            DateTime utcUtc,
            ProjectionModel projectionModel,
            double horizonPaddingPct,
            bool flipHorizontal,
            double fovDeg,
            bool applyRefraction = false,
            StarSizeCurve? sizeCurve = null)
            : this(new FisheyeProjector(width, height, projectionModel, fovDeg, horizonPaddingPct),
                   latitudeDeg, longitudeDeg, utcUtc, flipHorizontal, applyRefraction, sizeCurve)
        {
        }

    public int Width  => _projector.WidthPx;
    public int Height => _projector.HeightPx;
    public IImageProjector Projector => _projector;

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
            static bool ShouldMicroDot(double mag, float radius) =>
                (mag >= 5.2) || (radius < 1.05f);

            projectedStars = new List<StarProjection>(stars.Count + randomFillerCount);
            projectedPlanets = planets.Count == 0 ? new List<PlanetProjection>() : new List<PlanetProjection>(planets.Count);

            var bitmap = new SKBitmap(Width, Height, true);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

            var lstHours = LocalSiderealTime(_utc, _longitudeDeg);
            var latitudeRad = DegreesToRadians(_latitudeDeg);

            var scaleRatio = (float)(Math.Min(Width, Height) * 0.5 / 480.0); // baseline 480 px “fisheye radius”

            // ---- Stars ----
            for (var i = 0; i < stars.Count; i++)
            {
                var s = stars[i];
                RaDecToAltAz(s.RightAscensionHours, s.DeclinationDegrees, lstHours, latitudeRad, out var altDeg, out var azDeg);

                if (_applyRefraction) altDeg += BennettRefractionDeg(altDeg);
                if (altDeg < 0) continue;

                var (X, Y, Z) = AltAzToCameraRay(altDeg, azDeg, _flipHorizontal);

                // NOTE: In your codebase TryProjectRay appears to be 'void'. Call and then bounds-check.
                _projector.TryProjectRay(X, Y, Z, out var px, out var py);
                if (px < 0 || px >= Width || py < 0 || py >= Height) continue;

                var magnitude = s.Magnitude;
                var radius = RadiusFromMagnitude(magnitude, _sizeCurve, scaleRatio);

                var color = s.Color ?? SKColors.White;
                if (dimFaintStars)
                {
                    var a = AlphaFromMagnitude(magnitude);
                    color = new SKColor(color.Red, color.Green, color.Blue, a);
                }

                bool micro2x2 = magnitude >= 6.0;
                bool micro1x1 = (magnitude >= 5.2) || (radius < 1.05f);

                if (micro2x2)
                {
                    var pxI = (int)Math.Round(px);
                    var pyI = (int)Math.Round(py);
                    var aa = paint.IsAntialias;
                    paint.IsAntialias = false;
                    paint.Color = color;
                    canvas.DrawRect(SKRect.Create(pxI, pyI, 2, 2), paint);
                    paint.IsAntialias = aa;
                }
                else if (micro1x1)
                {
                    var pxI = (int)Math.Round(px);
                    var pyI = (int)Math.Round(py);
                    var aa = paint.IsAntialias;
                    paint.IsAntialias = false;
                    paint.Color = color;
                    canvas.DrawRect(SKRect.Create(pxI, pyI, 1, 1), paint);
                    paint.IsAntialias = aa;
                }
                else
                {
                    paint.Color = color;
                    canvas.DrawCircle(px, py, Math.Max(1.0f, radius), paint);
                }

                projectedStars.Add(new StarProjection(i, px, py, magnitude));
            }

            // ---- Optional random filler ----
            if (randomFillerCount > 0)
            {
                var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
                for (var i = 0; i < randomFillerCount; i++)
                {
                    var raHours = rng.NextDouble() * 24.0;
                    var sinDec = rng.NextDouble() * 2.0 - 1.0;
                    var decDegrees = RadiansToDegrees(Math.Asin(Math.Clamp(sinDec, -1.0, 1.0)));
                    var mag = rng.NextDouble() < 0.67 ? 5.0 + rng.NextDouble() * 2.0 : 3.0 + rng.NextDouble() * 2.0;

                    RaDecToAltAz(raHours, decDegrees, lstHours, latitudeRad, out var altDeg, out var azDeg);

                    if (_applyRefraction) altDeg += BennettRefractionDeg(altDeg);
                    if (altDeg < 0) continue;

                    var (X, Y, Z) = AltAzToCameraRay(altDeg, azDeg, _flipHorizontal);

                    _projector.TryProjectRay(X, Y, Z, out var px, out var py);
                    if (px < 0 || px >= Width || py < 0 || py >= Height) continue;

                    var radius = RadiusFromMagnitude(mag, _sizeCurve, scaleRatio);
                    var a = dimFaintStars ? AlphaFromMagnitude(mag) : (byte)210;
                    var color = mag > 5.6
                        ? new SKColor(170, 170, 170, a)
                        : new SKColor(210, 210, 210, a);

                    if (ShouldMicroDot(mag, radius))
                    {
                        var pxI = (int)Math.Round(px);
                        var pyI = (int)Math.Round(py);
                        var aa = paint.IsAntialias;
                        paint.IsAntialias = false;
                        paint.Color = color;
                        canvas.DrawRect(SKRect.Create(pxI, pyI, 1, 1), paint);
                        paint.IsAntialias = aa;
                    }
                    else
                    {
                        paint.Color = color;
                        canvas.DrawCircle(px, py, Math.Max(1.0f, radius), paint);
                    }

                    projectedStars.Add(new StarProjection(-1, px, py, mag));
                }
            }

            // ---- Planets ----
            if (planets.Count > 0)
            {
                foreach (var p in planets)
                {
                    RaDecToAltAz(p.Star.RightAscensionHours, p.Star.DeclinationDegrees, lstHours, latitudeRad, out var altDeg, out var azDeg);

                    if (_applyRefraction) altDeg += BennettRefractionDeg(altDeg);
                    if (altDeg < 0) continue;

                    var (X, Y, Z) = AltAzToCameraRay(altDeg, azDeg, _flipHorizontal);

                    _projector.TryProjectRay(X, Y, Z, out var px, out var py);
                    if (px < 0 || px >= Width || py < 0 || py >= Height) continue;

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
        {
            var lstHours = LocalSiderealTime(_utc, _longitudeDeg);
            var latitudeRad = DegreesToRadians(_latitudeDeg);
            x = y = 0f;

            RaDecToAltAz(star.RightAscensionHours, star.DeclinationDegrees, lstHours, latitudeRad, out var altDeg, out var azDeg);
            if (_applyRefraction) altDeg += BennettRefractionDeg(altDeg);
            if (altDeg < 0) return false;

            var ray = AltAzToCameraRay(altDeg, azDeg, _flipHorizontal);
            _projector.TryProjectRay(ray.X, ray.Y, ray.Z, out x, out y);
            return !(x < 0 || x >= Width || y < 0 || y >= Height);
        }

        private static (double X, double Y, double Z) AltAzToCameraRay(double altitudeDeg, double azimuthDeg, bool flipHorizontal)
        {
            var alt = DegreesToRadians(altitudeDeg);
            var az  = DegreesToRadians(azimuthDeg);
            var cosAlt = Math.Cos(alt);
            var sinAlt = Math.Sin(alt);

            // ENU: x=East, y=North, z=Up
            var xEast  = cosAlt * Math.Sin(az);
            var yNorth = cosAlt * Math.Cos(az);
            var zUp    = sinAlt;

            var X = flipHorizontal ? -xEast : xEast;
            var Y = yNorth;
            var Z = zUp;

            var len = Math.Sqrt(X*X + Y*Y + Z*Z);
            return (X/len, Y/len, Z/len);
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

        private static void RaDecToAltAz(double raHours, double decDeg, double lstHours, double latitudeRad, out double altitudeDeg, out double azimuthDeg)
        {
            var hourAngle = DegreesToRadians((lstHours - raHours) * 15.0);
            var decRad = DegreesToRadians(decDeg);
            var sinAlt = Math.Sin(decRad) * Math.Sin(latitudeRad) + Math.Cos(decRad) * Math.Cos(latitudeRad) * Math.Cos(hourAngle);
            var altitude = Math.Asin(Math.Clamp(sinAlt, -1.0, 1.0));

            var cosAz = (Math.Sin(decRad) - Math.Sin(altitude) * Math.Sin(latitudeRad)) / (Math.Cos(altitude) * Math.Cos(latitudeRad));
            cosAz = Math.Clamp(cosAz, -1.0, 1.0);

            var azimuth = Math.Acos(cosAz);
            if (Math.Sin(hourAngle) > 0) azimuth = Math.PI * 2.0 - azimuth;

            altitudeDeg = RadiansToDegrees(altitude);
            azimuthDeg = RadiansToDegrees(azimuth);
        }

        private static double BennettRefractionDeg(double altDeg)
        {
            var a = Math.Max(altDeg, -0.9);
            var rArcMin = 1.02 / Math.Tan((a + 10.3 / (a + 5.11)) * Math.PI / 180.0);
            return rArcMin / 60.0;
        }

        public static double LocalSiderealTime(DateTime utc, double longitudeDeg)
        {
            var jd = utc.ToOADate() + 2415018.5;
            var t = (jd - 2451545.0) / 36525.0;
            var gmst = 6.697374558 + 2400.051336 * t + 0.000025862 * t * t;
            var fractionalDay = (jd + 0.5) % 1.0;
            gmst = (gmst + fractionalDay * 24.0 * 1.00273790935) % 24.0;
            var lst = (gmst + longitudeDeg / 15.0) % 24.0;
            if (lst < 0) lst += 24.0;
            return lst;
        }

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
                _ => 175 - 10.0 * (mag - 5.0)
            };
            return (byte)Math.Clamp(a, 165, 255);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_projector is IDisposable disposableProjector)
            {
                disposableProjector.Dispose();
            }

            _disposed = true;
        }
    }
}
