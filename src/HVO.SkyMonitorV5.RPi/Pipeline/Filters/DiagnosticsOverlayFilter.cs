#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

/// <summary>
/// Renders a lightweight diagnostics block containing stacking metrics, rig details, and projector geometry.
/// </summary>
public sealed class DiagnosticsOverlayFilter : IFrameFilter
{
    private readonly IOptionsMonitor<DiagnosticsOverlayOptions> _optionsMonitor;
    private readonly ILogger<DiagnosticsOverlayFilter> _logger;

    public DiagnosticsOverlayFilter(IOptionsMonitor<DiagnosticsOverlayOptions> optionsMonitor, ILogger<DiagnosticsOverlayFilter> logger)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => FrameFilterNames.DiagnosticsOverlay;

    public bool ShouldApply(CameraConfiguration configuration)
    {
        if (!configuration.EnableImageOverlays)
        {
            return false;
        }

        return _optionsMonitor.CurrentValue.Enabled;
    }

    public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
        => ApplyAsync(bitmap, stackResult, configuration, renderContext: null, cancellationToken);

    public ValueTask ApplyAsync(
        SKBitmap bitmap,
        FrameStackResult stackResult,
        CameraConfiguration configuration,
        FrameRenderContext? renderContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return ValueTask.CompletedTask;
        }

        if (renderContext is null)
        {
            _logger.LogWarning("Diagnostics overlay skipped; FrameRenderContext unavailable.");
            return ValueTask.CompletedTask;
        }

        var lines = BuildLines(stackResult, renderContext, options);
        if (lines.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        using var canvas = new SKCanvas(bitmap);
        using var titleTypeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.SemiBold);
        using var bodyTypeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.Normal);
        using var titleFont = new SKFont(titleTypeface, options.TitleFontSize);
        using var bodyFont = new SKFont(bodyTypeface, options.BodyFontSize);
        using var titlePaint = new SKPaint { IsAntialias = true, Color = new SKColor(200, 225, 255, 235) };
        using var bodyPaint = new SKPaint { IsAntialias = true, Color = new SKColor(240, 240, 240, 220) };
        using var backgroundPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 170) };

        var measuredLines = MeasureLines(lines, titleFont, bodyFont, titlePaint, bodyPaint, cancellationToken);
        if (measuredLines.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        var margin = options.Margin;
        var lineSpacing = options.LineSpacing;

        var contentWidth = 0f;
        var contentHeight = 0f;

        for (var i = 0; i < measuredLines.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = measuredLines[i];
            contentWidth = Math.Max(contentWidth, line.Width);
            contentHeight += line.Height;
        }

        if (measuredLines.Count > 1)
        {
            contentHeight += lineSpacing * (measuredLines.Count - 1);
        }

        var boxWidth = contentWidth + margin * 2f;
        var boxHeight = contentHeight + margin * 2f;

        var rect = CalculateRect(bitmap.Width, bitmap.Height, boxWidth, boxHeight, margin, options.Corner);

        using (var path = new SKPath())
        {
            path.AddRoundRect(rect, 12f, 12f);
            canvas.DrawPath(path, backgroundPaint);
        }

        var baseline = rect.Top + margin;

        for (var i = 0; i < measuredLines.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = measuredLines[i];
            var paint = line.IsTitle ? titlePaint : bodyPaint;
            var font = line.IsTitle ? titleFont : bodyFont;

            var textBaseline = baseline - line.Metrics.Ascent;
            canvas.DrawText(line.Text, rect.Left + margin, textBaseline, SKTextAlign.Left, font, paint);

            baseline += line.Height;
            if (i < measuredLines.Count - 1)
            {
                baseline += lineSpacing;
            }
        }

        return ValueTask.CompletedTask;
    }

    private static List<(string Text, bool IsTitle)> BuildLines(
        FrameStackResult stackResult,
        FrameRenderContext context,
        DiagnosticsOverlayOptions options)
    {
        var lines = new List<(string Text, bool IsTitle)>
        {
            ($"Diagnostics", true)
        };

        if (options.ShowStackingMetrics)
        {
            lines.Add(($"Stacked Frames: {stackResult.FramesStacked}", false));
            lines.Add(($"Integration: {stackResult.IntegrationMilliseconds} ms", false));
            lines.Add(($"Exposure: {stackResult.Exposure.ExposureMilliseconds} ms | Gain {stackResult.Exposure.Gain}", false));
        }

        if (options.ShowRigDetails)
        {
            var rig = context.Rig;
            var sensor = rig.Sensor;
            lines.Add(($"Rig: {rig.Name}", false));
            lines.Add(($"Sensor: {sensor.WidthPx}x{sensor.HeightPx} @ {sensor.PixelSizeMicrons:F2}µm", false));
        }

        if (options.ShowProjectorDetails)
        {
            var projector = context.Projector;
            var horizon = context.HorizonPadding.HasValue
                ? context.HorizonPadding.Value.ToString("F2", CultureInfo.InvariantCulture)
                : "n/a";
            lines.Add(($"Projector: {projector.WidthPx}x{projector.HeightPx} | Horizon {horizon}", false));
        }

        if (options.ShowContextFlags)
        {
            lines.Add(($"Flip Horizontal: {context.FlipHorizontal}", false));
            lines.Add(($"Refraction: {context.ApplyRefraction}", false));
            lines.Add(($"Lat/Lon: {context.LatitudeDeg:F4}, {context.LongitudeDeg:F4}", false));
        }

        return lines;
    }

    private static List<MeasuredLine> MeasureLines(
        List<(string Text, bool IsTitle)> lines,
        SKFont titleFont,
        SKFont bodyFont,
        SKPaint titlePaint,
        SKPaint bodyPaint,
        CancellationToken cancellationToken)
    {
        var measured = new List<MeasuredLine>(lines.Count);

        foreach (var (text, isTitle) in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var font = isTitle ? titleFont : bodyFont;
            var paint = isTitle ? titlePaint : bodyPaint;
            var width = font.MeasureText(text, paint);
            var metrics = font.Metrics;
            var height = metrics.Descent - metrics.Ascent;
            measured.Add(new MeasuredLine(text, isTitle, width, height, metrics));
        }

        return measured;
    }

    private static SKRect CalculateRect(int canvasWidth, int canvasHeight, float boxWidth, float boxHeight, float margin, OverlayCorner corner)
    {
        float left;
        float top;

        switch (corner)
        {
            case OverlayCorner.TopLeft:
                left = margin;
                top = margin;
                break;
            case OverlayCorner.TopRight:
                left = Math.Max(margin, canvasWidth - boxWidth - margin);
                top = margin;
                break;
            case OverlayCorner.BottomLeft:
                left = margin;
                top = Math.Max(margin, canvasHeight - boxHeight - margin);
                break;
            case OverlayCorner.BottomRight:
                left = Math.Max(margin, canvasWidth - boxWidth - margin);
                top = Math.Max(margin, canvasHeight - boxHeight - margin);
                break;
            default:
                left = margin;
                top = margin;
                break;
        }

        return new SKRect(left, top, left + boxWidth, top + boxHeight);
    }

    private readonly record struct MeasuredLine(string Text, bool IsTitle, float Width, float Height, SKFontMetrics Metrics);
}
