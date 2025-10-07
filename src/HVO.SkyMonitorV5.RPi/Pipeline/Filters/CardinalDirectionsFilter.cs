#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Simulation;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

public sealed class CardinalDirectionsFilter : IFrameFilter
{
    private readonly IOptionsMonitor<ObservatoryLocationOptions> _locationMonitor;

    public CardinalDirectionsFilter(IOptionsMonitor<ObservatoryLocationOptions> locationMonitor)
    {
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));
    }

    public string Name => FrameFilterNames.CardinalDirections;

    public bool ShouldApply(CameraConfiguration configuration) => configuration.EnableImageOverlays;

    public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

    var location = _locationMonitor.CurrentValue;
    var center = SkySimulationMath.GetSkyCenter(bitmap.Width, bitmap.Height, location.LatitudeDegrees);
        var radius = SkySimulationMath.GetSkyRadius(bitmap.Width, bitmap.Height);

        using var canvas = new SKCanvas(bitmap);
        using var ringPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(200, 210, 230, 150),
            StrokeWidth = 2f,
            PathEffect = SKPathEffect.CreateDash(new float[] { 14f, 12f }, 0f)
        };

        canvas.DrawCircle(center, radius, ringPaint);

        using var tickPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(200, 210, 230, 170),
            StrokeWidth = 2f
        };

        var cardinals = new (string Label, float AngleDegrees)[]
        {
            ("N", 90f),
            ("E", 0f),
            ("S", -90f),
            ("W", 180f)
        };

        using var labelTypeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.Bold);
        using var labelFont = new SKFont(labelTypeface, 22);
        using var labelPaint = new SKPaint { IsAntialias = true, Color = new SKColor(225, 235, 255, 220) };
        var labelMetrics = labelFont.Metrics;

        foreach (var (label, angleDegrees) in cardinals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var angleRad = MathF.PI / 180f * angleDegrees;
            var outer = new SKPoint(
                center.X + radius * MathF.Cos(angleRad),
                center.Y - radius * MathF.Sin(angleRad));
            var inner = new SKPoint(
                center.X + (radius - 16f) * MathF.Cos(angleRad),
                center.Y - (radius - 16f) * MathF.Sin(angleRad));
            canvas.DrawLine(inner, outer, tickPaint);

            var textPos = new SKPoint(
                center.X + (radius + 20f) * MathF.Cos(angleRad),
                center.Y - (radius + 22f) * MathF.Sin(angleRad));
            canvas.DrawText(label, textPos.X, textPos.Y + labelMetrics.CapHeight / 2f, SKTextAlign.Center, labelFont, labelPaint);
        }

        return ValueTask.CompletedTask;
    }
}
