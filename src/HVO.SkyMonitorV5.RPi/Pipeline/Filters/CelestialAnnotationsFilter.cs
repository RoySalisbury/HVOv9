#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Simulation;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

public sealed class CelestialAnnotationsFilter : IFrameFilter
{
    private readonly IOptionsMonitor<ObservatoryLocationOptions> _locationMonitor;

    public CelestialAnnotationsFilter(IOptionsMonitor<ObservatoryLocationOptions> locationMonitor)
    {
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));
    }

    private static readonly IReadOnlyList<CelestialAnnotation> CelestialAnnotations = new List<CelestialAnnotation>
    {
        new("Polaris", 0.14f, 90f, new SKColor(255, 255, 224, 230)),
        new("Mars", 0.88f, 205f, new SKColor(255, 180, 120, 230)),
        new("Saturn", 0.68f, 150f, new SKColor(255, 228, 181, 230)),
        new("Spica", 0.7f, 140f, new SKColor(255, 192, 203, 230)),
        new("Altair", 0.52f, 55f, new SKColor(173, 216, 230, 230)),
        new("Vega", 0.46f, 40f, new SKColor(173, 216, 230, 230)),
        new("Deneb", 0.55f, 80f, new SKColor(173, 216, 230, 230)),
        new("Fomalhaut", 0.82f, 330f, new SKColor(255, 182, 193, 230)),
        new("Neptune", 0.8f, 315f, new SKColor(100, 149, 237, 230)),
        new("Achernar", 0.9f, 255f, new SKColor(255, 182, 193, 230))
    };

    public string Name => FrameFilterNames.CelestialAnnotations;

    public bool ShouldApply(CameraConfiguration configuration) => configuration.EnableImageOverlays;

    public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

    var location = _locationMonitor.CurrentValue;
    var center = SkySimulationMath.GetSkyCenter(bitmap.Width, bitmap.Height, location.LatitudeDegrees);
    var radius = SkySimulationMath.GetSkyRadius(bitmap.Width, bitmap.Height);
    var rotationDegrees = SkySimulationMath.CalculateSkyRotationDegrees(stackResult.Frame.Timestamp, location.LongitudeDegrees);

        using var canvas = new SKCanvas(bitmap);
        using var markerPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textTypeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.Normal);
        using var textFont = new SKFont(textTypeface, 16);
        using var haloPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 140) };

        foreach (var annotation in CelestialAnnotations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var angleRad = (float)Math.PI / 180f * (annotation.AzimuthDegrees + rotationDegrees);
            var distance = radius * annotation.RadiusFraction;
            var position = new SKPoint(
                center.X + distance * MathF.Cos(angleRad),
                center.Y - distance * MathF.Sin(angleRad));

            markerPaint.Color = annotation.Color;
            canvas.DrawCircle(position, 4f, haloPaint);
            canvas.DrawCircle(position, 3f, markerPaint);

            var labelOffset = new SKPoint(8f, -8f);
            var textPosition = new SKPoint(position.X + labelOffset.X, position.Y + labelOffset.Y);
            canvas.DrawText(annotation.Name, textPosition.X, textPosition.Y, SKTextAlign.Left, textFont, markerPaint);
        }

        return ValueTask.CompletedTask;
    }

    private sealed record CelestialAnnotation(string Name, float RadiusFraction, float AzimuthDegrees, SKColor Color);
}
