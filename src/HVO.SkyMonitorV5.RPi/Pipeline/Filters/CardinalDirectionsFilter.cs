#nullable enable
using System;
using System.Collections.Generic;
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
        private readonly IOptionsMonitor<CardinalDirectionsOptions> _opts;
        private readonly ILogger<CardinalDirectionsFilter> _logger;

        public CardinalDirectionsFilter(
            IOptionsMonitor<CardinalDirectionsOptions> options,
            ILogger<CardinalDirectionsFilter> logger)
        {
            _opts = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => FrameFilterNames.CardinalDirections;

        public bool ShouldApply(CameraConfiguration configuration)
        {
            // Only gate on top-level overlays flag; per-option toggles can be added later.
            return configuration.EnableImageOverlays;
        }

        public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stack, CameraConfiguration configuration, CancellationToken cancellationToken)
            => ApplyAsync(bitmap, stack, configuration, renderContext: null, cancellationToken);

        public ValueTask ApplyAsync(
            SKBitmap bitmap,
            FrameStackResult stack,
            CameraConfiguration configuration,
            FrameRenderContext? renderContext,
            CancellationToken cancellationToken)
        {
            _ = renderContext;
            cancellationToken.ThrowIfCancellationRequested();

            var options = _opts.CurrentValue;

            using var canvas = new SKCanvas(bitmap);
            canvas.Save();

            var center = new SKPoint(
                bitmap.Width / 2f + options.OffsetXPixels,
                bitmap.Height / 2f + options.OffsetYPixels);

            var radiusBase = Math.Min(bitmap.Width, bitmap.Height) / 2f;
            var radius = Math.Max(8f, radiusBase + options.RadiusOffsetPixels);

            var circleColor = ResolveColor(options.CircleColor, new SKColor(200, 213, 230));
            var circlePaint = new SKPaint
            {
                IsAntialias = true,
                Color = circleColor.WithAlpha((byte)Math.Clamp(options.CircleOpacity, 0, 255)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(0.5f, options.CircleThickness)
            };

            using var dashEffect = ResolveCircleDashEffect(options.CircleLineStyle, circlePaint.StrokeWidth);
            circlePaint.PathEffect = dashEffect;

            canvas.DrawCircle(center, radius, circlePaint);

            _logger.LogTrace("Rendering cardinal directions overlay at ({CenterX},{CenterY}) with radius {Radius}px and rotation {RotationDegrees}°", center.X, center.Y, radius, options.RotationDegrees);

            using var typeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.Bold);
            using var font = new SKFont(typeface, options.LabelFontSize);
            using var textPaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White
            };

            var labelBgColor = new SKColor(0, 0, 0).WithAlpha((byte)Math.Clamp(options.LabelFillOpacity, 0, 255));
            using var labelBgPaint = new SKPaint
            {
                IsAntialias = true,
                Color = labelBgColor,
                Style = SKPaintStyle.Fill
            };
            using var labelBorderPaint = new SKPaint
            {
                IsAntialias = true,
                Color = circleColor.WithAlpha(circlePaint.Color.Alpha),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(0.5f, options.CircleThickness)
            };

            var labels = BuildLabelMap(options);

            var metrics = font.Metrics;
            var textHeight = metrics.Descent - metrics.Ascent;
            var labelRadius = Math.Max(0f, radius - options.LabelPadding - textHeight * 0.5f - circlePaint.StrokeWidth);
            var rotationOffset = DegreesToRadians(options.RotationDegrees);

            foreach (var entry in labels)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var angleDeg = entry.AngleDegrees;
                var angle = DegreesToRadians(angleDeg) + rotationOffset;

                var position = new SKPoint(
                    center.X + labelRadius * (float)Math.Cos(angle),
                    center.Y + labelRadius * (float)Math.Sin(angle));

                var label = entry.Label ?? string.Empty;
                var textWidth = font.MeasureText(label, textPaint);

                var padding = options.LabelPadding;
                var rect = new SKRect(
                    position.X - textWidth / 2f - padding,
                    position.Y - textHeight / 2f - padding,
                    position.X + textWidth / 2f + padding,
                    position.Y + textHeight / 2f + padding);

                if (labelBgPaint.Color.Alpha > 0)
                {
                    if (options.LabelCornerRadius <= 0f)
                    {
                        canvas.DrawRect(rect, labelBgPaint);
                    }
                    else
                    {
                        canvas.DrawRoundRect(rect, options.LabelCornerRadius, options.LabelCornerRadius, labelBgPaint);
                    }
                }

                if (options.LabelCornerRadius <= 0f)
                {
                    canvas.DrawRect(rect, labelBorderPaint);
                }
                else
                {
                    canvas.DrawRoundRect(rect, options.LabelCornerRadius, options.LabelCornerRadius, labelBorderPaint);
                }

                var textX = rect.MidX - textWidth / 2f;
                var textY = rect.MidY - (metrics.Ascent + metrics.Descent) / 2f;
                canvas.DrawText(label, textX, textY, font, textPaint);
            }

            canvas.Restore();

            return ValueTask.CompletedTask;
        }

        private static SKPathEffect? ResolveCircleDashEffect(CardinalLineStyle style, float strokeWidth)
            => style switch
            {
                CardinalLineStyle.Solid => null,
                CardinalLineStyle.LongDash => SKPathEffect.CreateDash(new[] { 24f, 12f }, 0f),
                CardinalLineStyle.ShortDash => SKPathEffect.CreateDash(new[] { 12f, 12f }, 0f),
                CardinalLineStyle.Dotted => SKPathEffect.CreateDash(new[] { strokeWidth, strokeWidth * 1.6f }, 0f),
                CardinalLineStyle.DashDot => SKPathEffect.CreateDash(new[] { 20f, 10f, strokeWidth * 1.2f, 10f }, 0f),
                _ => null
            };

        private static IReadOnlyList<(string Label, float AngleDegrees)> BuildLabelMap(CardinalDirectionsOptions options)
        {
            var eastLabel = options.SwapEastWest ? options.LabelWest : options.LabelEast;
            var westLabel = options.SwapEastWest ? options.LabelEast : options.LabelWest;

            return new[]
            {
                (options.LabelNorth, -90f),
                (eastLabel, 0f),
                (options.LabelSouth, 90f),
                (westLabel, 180f)
            };
        }

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

            if (span.Length is 6 or 8 && uint.TryParse(span, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                if (span.Length == 6)
                {
                    return new SKColor(
                        (byte)((value & 0xFF0000) >> 16),
                        (byte)((value & 0x00FF00) >> 8),
                        (byte)(value & 0x0000FF));
                }

                return new SKColor(
                    (byte)((value & 0x00FF0000) >> 16),
                    (byte)((value & 0x0000FF00) >> 8),
                    (byte)(value & 0x000000FF),
                    (byte)((value & 0xFF000000) >> 24));
            }

            return fallback;
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
    }
}
