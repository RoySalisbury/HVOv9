#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline.Overlays;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters
{
    /// <summary>
    /// Draws simple N/E/S/W markers using the shared StarFieldEngine provided by the pipeline.
    /// </summary>
    public sealed class CardinalDirectionsFilter : IImageFrameFilter, IDisposable
    {
        private readonly IOptionsMonitor<CardinalDirectionsOptions> _opts;
        private readonly ILogger<CardinalDirectionsFilter> _logger;
        private readonly OverlayAssetCache _assetCache;
        private readonly IDisposable? _optionsReload;

        private const string CacheGroup = "CardinalDirections";

        public CardinalDirectionsFilter(
            IOptionsMonitor<CardinalDirectionsOptions> options,
            ILogger<CardinalDirectionsFilter> logger,
            OverlayAssetCache assetCache)
        {
            _opts = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _assetCache = assetCache ?? throw new ArgumentNullException(nameof(assetCache));

            _optionsReload = _opts.OnChange(_ => _assetCache.InvalidateGroup(CacheGroup));
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
            cancellationToken.ThrowIfCancellationRequested();

            var options = _opts.CurrentValue;

            using var canvas = new SKCanvas(bitmap);
            RenderOverlay(canvas, bitmap.Width, bitmap.Height, renderContext, options, cancellationToken);

            return ValueTask.CompletedTask;
        }

        public ValueTask ApplyAsync(
            FilterFrame frame,
            FrameStackResult stack,
            CameraConfiguration configuration,
            FrameRenderContext? renderContext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var options = _opts.CurrentValue;
            var canvas = frame.Surface.Canvas;

            var width = stack.StackedImmutableImage?.Width
                ?? stack.StackedImage?.Width
                ?? canvas.DeviceClipBounds.Width;
            var height = stack.StackedImmutableImage?.Height
                ?? stack.StackedImage?.Height
                ?? canvas.DeviceClipBounds.Height;

            if (width <= 0 || height <= 0)
            {
                var bounds = canvas.DeviceClipBounds;
                width = bounds.Width;
                height = bounds.Height;
            }

            canvas.Save();
            try
            {
                RenderOverlay(canvas, width, height, renderContext, options, cancellationToken);
            }
            finally
            {
                canvas.Restore();
                canvas.Flush();
            }

            return ValueTask.CompletedTask;
        }

        private void RenderOverlay(
            SKCanvas canvas,
            int width,
            int height,
            FrameRenderContext? renderContext,
            CardinalDirectionsOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = ComputeRenderParameters(width, height, renderContext, options);
            if (!parameters.IsValid)
            {
                return;
            }

            var cacheKey = BuildCacheKey(parameters, options);
            var picture = _assetCache.GetOrCreatePicture(cacheKey, () => CreatePicture(parameters, options));
            canvas.DrawPicture(picture);
        }

        private CardinalRenderParameters ComputeRenderParameters(
            int width,
            int height,
            FrameRenderContext? renderContext,
            CardinalDirectionsOptions options)
        {
            if (width <= 0 || height <= 0)
            {
                return CardinalRenderParameters.Invalid;
            }

            var projector = renderContext?.Projector;
            var center = projector is not null
                ? new SKPoint((float)projector.Cx, (float)projector.Cy)
                : new SKPoint(width / 2f, height / 2f);

            var swapEastWest = options.SwapEastWest;
            var rotationDegrees = options.RotationDegrees;

            if (renderContext is not null)
            {
                if (renderContext.FlipHorizontal)
                {
                    swapEastWest = !swapEastWest;
                }

                var rigRoll = renderContext.Rig.Lens.RollDeg;
                if (Math.Abs(rigRoll) > double.Epsilon)
                {
                    rotationDegrees += (float)rigRoll;
                }
            }

            center.Offset(options.OffsetXPixels, options.OffsetYPixels);

            var radiusBase = Math.Min(width, height) / 2f;
            var radius = Math.Max(8f, radiusBase + options.RadiusOffsetPixels);

            return new CardinalRenderParameters(width, height, center, radius, rotationDegrees, swapEastWest, true);
        }

        private string BuildCacheKey(CardinalRenderParameters parameters, CardinalDirectionsOptions options)
        {
            var hash = new HashCode();
            hash.Add(parameters.Width);
            hash.Add(parameters.Height);
            hash.Add(BitConverter.SingleToInt32Bits(parameters.Center.X));
            hash.Add(BitConverter.SingleToInt32Bits(parameters.Center.Y));
            hash.Add(BitConverter.SingleToInt32Bits(parameters.Radius));
            hash.Add((int)Math.Round(parameters.RotationDegrees * 100.0f));
            hash.Add(parameters.SwapEastWest);

            hash.Add(options.SwapEastWest);
            hash.Add(options.CircleColor, StringComparer.Ordinal);
            hash.Add(options.CircleOpacity);
            hash.Add(options.CircleThickness);
            hash.Add((int)Math.Round(options.LabelFontSize * 100f));
            hash.Add(options.LabelNorth, StringComparer.Ordinal);
            hash.Add(options.LabelEast, StringComparer.Ordinal);
            hash.Add(options.LabelSouth, StringComparer.Ordinal);
            hash.Add(options.LabelWest, StringComparer.Ordinal);
            hash.Add(options.LabelPadding);
            hash.Add(options.LabelCornerRadius);
            hash.Add(options.LabelFillOpacity);
            hash.Add(options.RadiusOffsetPixels);
            hash.Add(options.OffsetXPixels);
            hash.Add(options.OffsetYPixels);
            hash.Add((int)options.CircleLineStyle);

            return FormattableString.Invariant($"{CacheGroup}:{hash.ToHashCode():X8}");
        }

        private SKPicture CreatePicture(CardinalRenderParameters parameters, CardinalDirectionsOptions options)
        {
            using var recorder = new SKPictureRecorder();
            var bounds = SKRect.Create(parameters.Width, parameters.Height);
            var recordingCanvas = recorder.BeginRecording(bounds);

            DrawCardinalOverlay(recordingCanvas, parameters, options);

            return recorder.EndRecording();
        }

        private void DrawCardinalOverlay(SKCanvas canvas, CardinalRenderParameters parameters, CardinalDirectionsOptions options)
        {
            var circleColor = ResolveColor(options.CircleColor, new SKColor(200, 213, 230));
            using var circlePaint = new SKPaint
            {
                IsAntialias = true,
                Color = circleColor.WithAlpha((byte)Math.Clamp(options.CircleOpacity, 0, 255)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(0.5f, options.CircleThickness)
            };

            using var dashEffect = ResolveCircleDashEffect(options.CircleLineStyle, circlePaint.StrokeWidth);
            circlePaint.PathEffect = dashEffect;

            canvas.DrawCircle(parameters.Center, parameters.Radius, circlePaint);

            _logger.LogTrace(
                "Rendering cardinal directions overlay at ({CenterX},{CenterY}) with radius {Radius}px and rotation {RotationDegrees}°",
                parameters.Center.X,
                parameters.Center.Y,
                parameters.Radius,
                parameters.RotationDegrees);

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

            var labels = BuildLabelMap(options, parameters.SwapEastWest);

            var metrics = font.Metrics;
            var textHeight = metrics.Descent - metrics.Ascent;
            var labelRadius = Math.Max(0f, parameters.Radius - options.LabelPadding - textHeight * 0.5f - circlePaint.StrokeWidth);
            var rotationOffset = DegreesToRadians(parameters.RotationDegrees);

            foreach (var entry in labels)
            {
                var angle = DegreesToRadians(entry.AngleDegrees) + rotationOffset;

                var position = new SKPoint(
                    parameters.Center.X + labelRadius * (float)Math.Cos(angle),
                    parameters.Center.Y + labelRadius * (float)Math.Sin(angle));

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

        private static IReadOnlyList<(string Label, float AngleDegrees)> BuildLabelMap(CardinalDirectionsOptions options, bool swapEastWest)
        {
            var eastLabel = swapEastWest ? options.LabelWest : options.LabelEast;
            var westLabel = swapEastWest ? options.LabelEast : options.LabelWest;

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

        private readonly record struct CardinalRenderParameters(
            int Width,
            int Height,
            SKPoint Center,
            float Radius,
            float RotationDegrees,
            bool SwapEastWest,
            bool IsValid)
        {
            public static CardinalRenderParameters Invalid { get; } = new(0, 0, SKPoint.Empty, 0f, 0f, false, false);
        }

        public void Dispose()
        {
            _optionsReload?.Dispose();
        }
    }
}
