#nullable enable

using System;
using System.Globalization;
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
    private readonly IOptionsMonitor<CardinalDirectionsOptions> _optionsMonitor;

    public CardinalDirectionsFilter(
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor,
        IOptionsMonitor<CardinalDirectionsOptions> optionsMonitor)
    {
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
    }

    public string Name => FrameFilterNames.CardinalDirections;

    public bool ShouldApply(CameraConfiguration configuration) => configuration.EnableImageOverlays;

    public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var overlayOptions = _optionsMonitor.CurrentValue;

        var baseCenter = new SKPoint(bitmap.Width / 2f, bitmap.Height / 2f);
        var center = new SKPoint(
            baseCenter.X + overlayOptions.OffsetXPixels,
            baseCenter.Y + overlayOptions.OffsetYPixels);
        var baseRadius = SkySimulationMath.GetSkyRadius(bitmap.Width, bitmap.Height);
        var radius = Math.Max(32f, baseRadius + overlayOptions.RadiusOffsetPixels);
        var tickLength = MathF.Min(16f, radius * 0.2f);

        var circleColor = ResolveColor(overlayOptions.CircleColor, new SKColor(200, 210, 230));
        var circleStrokeColor = circleColor.WithAlpha((byte)Math.Clamp(overlayOptions.CircleOpacity, 0, 255));
        var lineThickness = overlayOptions.CircleThickness;

        using var canvas = new SKCanvas(bitmap);
        using var ringPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = circleStrokeColor,
            StrokeWidth = lineThickness
        };

        using var tickPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = circleStrokeColor,
            StrokeWidth = lineThickness
        };

        ringPaint.PathEffect = CreatePathEffect(overlayOptions.CircleLineStyle, lineThickness);
        tickPaint.PathEffect = CreatePathEffect(overlayOptions.CircleLineStyle, lineThickness);

        canvas.DrawCircle(center, radius, ringPaint);

        static string ResolveLabel(string? label, string fallback) => string.IsNullOrWhiteSpace(label) ? fallback : label;

        var northLabel = ResolveLabel(overlayOptions.LabelNorth, "N");
        var southLabel = ResolveLabel(overlayOptions.LabelSouth, "S");
        var eastLabel = ResolveLabel(overlayOptions.LabelEast, "E");
        var westLabel = ResolveLabel(overlayOptions.LabelWest, "W");

        if (overlayOptions.SwapEastWest)
        {
            (eastLabel, westLabel) = (westLabel, eastLabel);
        }

        var cardinals = new (string Label, float AngleDegrees)[]
        {
            (northLabel, 90f),
            (eastLabel, 0f),
            (southLabel, -90f),
            (westLabel, 180f)
        };

        using var labelTypeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.Bold);
        using var labelFont = new SKFont(labelTypeface, overlayOptions.LabelFontSize);
        using var labelPaint = new SKPaint { IsAntialias = true, Color = new SKColor(225, 235, 255, 255) };
        var labelMetrics = labelFont.Metrics;
        var labelPadding = overlayOptions.LabelPadding;
        var cornerRadius = overlayOptions.LabelCornerRadius;
        var rotation = overlayOptions.RotationDegrees;

        using var labelStrokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = circleStrokeColor,
            StrokeWidth = lineThickness
        };

        foreach (var (label, angleDegrees) in cardinals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var angleRad = DegreesToRadians(angleDegrees + rotation);
            var outer = new SKPoint(
                center.X + radius * MathF.Cos(angleRad),
                center.Y - radius * MathF.Sin(angleRad));
            var innerRadius = Math.Max(radius - tickLength, radius * 0.6f);
            var inner = new SKPoint(
                center.X + innerRadius * MathF.Cos(angleRad),
                center.Y - innerRadius * MathF.Sin(angleRad));
            canvas.DrawLine(inner, outer, tickPaint);

            var textWidth = labelFont.MeasureText(label, labelPaint);
            var textHeight = labelMetrics.Descent - labelMetrics.Ascent;
            var halfWidth = textWidth / 2f + labelPadding;
            var halfHeight = textHeight / 2f + labelPadding;

            var labelClearance = Math.Max(halfHeight + lineThickness * 1.5f, tickLength + 4f);
            var labelRadius = radius - labelClearance;
            var minLabelRadius = Math.Max(radius * 0.4f, halfHeight + lineThickness * 1.5f);
            var maxLabelRadius = Math.Max(minLabelRadius, radius - tickLength);
            labelRadius = Math.Clamp(labelRadius, minLabelRadius, maxLabelRadius);

            var boxCenter = new SKPoint(
                center.X + labelRadius * MathF.Cos(angleRad),
                center.Y - labelRadius * MathF.Sin(angleRad));

            var boxRect = new SKRect(
                boxCenter.X - halfWidth,
                boxCenter.Y - halfHeight,
                boxCenter.X + halfWidth,
                boxCenter.Y + halfHeight);

            ClampRectToCanvas(ref boxRect, bitmap.Width, bitmap.Height);
            boxCenter = new SKPoint((boxRect.Left + boxRect.Right) / 2f, (boxRect.Top + boxRect.Bottom) / 2f);

            canvas.DrawRoundRect(boxRect, cornerRadius, cornerRadius, labelStrokePaint);

            var baseline = boxCenter.Y - (labelMetrics.Ascent + labelMetrics.Descent) / 2f;
            canvas.DrawText(label, boxCenter.X, baseline, SKTextAlign.Center, labelFont, labelPaint);
        }

        return ValueTask.CompletedTask;
    }

    private static float DegreesToRadians(float degrees) => (float)(Math.PI / 180d) * degrees;

    private static SKColor ResolveColor(string? color, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return fallback;
        }

        var span = color.AsSpan().Trim();

        if (span.StartsWith("#", StringComparison.Ordinal))
        {
            span = span[1..];
        }

        if (span.Length is 6 or 8 && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            if (span.Length == 6)
            {
                return new SKColor(
                    (byte)((value & 0xFF0000) >> 16),
                    (byte)((value & 0x00FF00) >> 8),
                    (byte)(value & 0x0000FF));
            }

            var r = (byte)((value & 0x00FF0000) >> 16);
            var g = (byte)((value & 0x0000FF00) >> 8);
            var b = (byte)(value & 0x000000FF);
            var a = (byte)((value & 0xFF000000) >> 24);
            return new SKColor(r, g, b, a);
        }

        return fallback;
    }

    private static SKPathEffect? CreatePathEffect(CardinalLineStyle style, float thickness) => style switch
    {
        CardinalLineStyle.Solid => null,
        CardinalLineStyle.LongDash => SKPathEffect.CreateDash(new[] { 18f, 12f }, 0f),
        CardinalLineStyle.ShortDash => SKPathEffect.CreateDash(new[] { 8f, 8f }, 0f),
        CardinalLineStyle.Dotted => SKPathEffect.CreateDash(new[] { Math.Max(thickness, 1f), Math.Max(thickness * 1.5f, 1.5f) }, 0f),
        CardinalLineStyle.DashDot => SKPathEffect.CreateDash(new[] { 16f, 10f, 2f, 10f }, 0f),
        _ => null
    };

    private static void ClampRectToCanvas(ref SKRect rect, int width, int height)
    {
        const float margin = 4f;
        var minX = margin;
        var minY = margin;
        var maxX = Math.Max(width - margin, minX);
        var maxY = Math.Max(height - margin, minY);

        var offsetX = 0f;
        if (rect.Left < minX)
        {
            offsetX = minX - rect.Left;
        }
        else if (rect.Right > maxX)
        {
            offsetX = maxX - rect.Right;
        }

        var offsetY = 0f;
        if (rect.Top < minY)
        {
            offsetY = minY - rect.Top;
        }
        else if (rect.Bottom > maxY)
        {
            offsetY = maxY - rect.Bottom;
        }

        rect.Offset(offsetX, offsetY);
    }
}
