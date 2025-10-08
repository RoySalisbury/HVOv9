#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters
{
    /// <summary>
    /// Draws simple N/E/S/W markers using the shared StarFieldEngine provided by the pipeline.
    /// </summary>
    public sealed class CardinalDirectionsFilter : IFrameFilter
    {
        private readonly IOptionsMonitor<ObservatoryLocationOptions> _loc;
        private readonly IOptionsMonitor<CardinalDirectionsOptions> _opts;
        private readonly IRenderEngineProvider _provider;
        private readonly ILogger<CardinalDirectionsFilter> _logger;

        public CardinalDirectionsFilter(
            IOptionsMonitor<ObservatoryLocationOptions> location,
            IOptionsMonitor<CardinalDirectionsOptions> options,
            IRenderEngineProvider provider,
            ILogger<CardinalDirectionsFilter> logger)
        {
            _loc = location ?? throw new ArgumentNullException(nameof(location));
            _opts = options ?? throw new ArgumentNullException(nameof(options));
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => FrameFilterNames.CardinalDirections;

        public bool ShouldApply(CameraConfiguration configuration)
        {
            // Only gate on top-level overlays flag; per-option toggles can be added later.
            return configuration.EnableImageOverlays;
        }

        public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stack, CameraConfiguration configuration, CancellationToken cancellationToken)
        {
            var engine = _provider.Current;
            if (engine is null) return ValueTask.CompletedTask;

            var altDeg = 5.0; // draw 5° above the horizon
            var labels = new (string Label, double AzimuthDeg)[]
            {
                ("N", 0.0),
                ("E", 90.0),
                ("S", 180.0),
                ("W", 270.0),
            };

            using var canvas = new SKCanvas(bitmap);
            using var typeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.Bold);
            using var font = new SKFont(typeface, 16f);
            using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(245,245,245,240) };
            using var halo = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 150) };

            var loc = _loc.CurrentValue;
            var lst = StarFieldEngine.LocalSiderealTime(stack.Frame.Timestamp.UtcDateTime, loc.LongitudeDegrees);
            var latRad = DegreesToRadians(loc.LatitudeDegrees);

            foreach (var (label, az) in labels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Alt/Az -> RA/Dec for the current site/time
                var (raHours, decDeg) = AltAzToRaDec(altDeg, az, lst, latRad);
                if (!engine.ProjectStar(new Star(raHours, decDeg, 2.0), out var x, out var y))
                    continue;

                // Halo box
                var textWidth = font.MeasureText(label, paint);
                var textHeight = font.Metrics.CapHeight;
                var rect = SKRect.Create(x - textWidth / 2f - 6f, y - textHeight - 10f, textWidth + 12f, textHeight + 8f);
                canvas.DrawRoundRect(rect, 4f, 4f, halo);

                // Text
                canvas.DrawText(label, rect.MidX - textWidth/2f, rect.Bottom - 4f, font, paint);
            }

            return ValueTask.CompletedTask;
        }

        private static (double RaHours, double DecDeg) AltAzToRaDec(double altDeg, double azDeg, double lstHours, double latRad)
        {
            // Convert Alt/Az to RA/Dec for az measured from North (0°) toward East (90°),
            // consistent with StarFieldEngine's azimuth.
            double alt = DegreesToRadians(altDeg);
            double az = DegreesToRadians(azDeg);

            double sinDec = Math.Sin(alt) * Math.Sin(latRad) + Math.Cos(alt) * Math.Cos(latRad) * Math.Cos(az);
            double dec = Math.Asin(Math.Clamp(sinDec, -1.0, 1.0));

            double cosH = (Math.Sin(alt) - Math.Sin(latRad) * Math.Sin(dec)) / (Math.Cos(latRad) * Math.Cos(dec));
            cosH = Math.Clamp(cosH, -1.0, 1.0);
            double sinH = -Math.Sin(az) * Math.Cos(alt) / Math.Cos(dec);

            double H = Math.Atan2(sinH, cosH); // hour angle (radians)
            double ra = (lstHours * Math.PI / 12.0) - H; // RA = LST - H
            // normalize
            ra %= (Math.PI * 2.0);
            if (ra < 0) ra += Math.PI * 2.0;

            double raHours = ra * 12.0 / Math.PI;
            double decDeg = dec * 180.0 / Math.PI;
            return (raHours, decDeg);
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
    }
}
