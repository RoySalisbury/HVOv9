#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using GeoTimeZone;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.Extensions.Options;
using SkiaSharp;
using TimeZoneConverter;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

public sealed class OverlayTextFilter : IImageFrameFilter, IDisposable
{
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;
    private readonly IOptionsMonitor<ObservatoryLocationOptions> _locationMonitor;
    private readonly object _timeZoneSync = new();
    private readonly object _overlaySync = new();
    private readonly IDisposable? _optionsReload;
    private CachedTimeZone? _cachedTimeZone;
    private CachedOverlayImage? _cachedOverlay;

    private static readonly SKColorSpace LinearSrgbColorSpace = SKColorSpace.CreateSrgbLinear();

    public OverlayTextFilter(
        IOptionsMonitor<CameraPipelineOptions> optionsMonitor,
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));

        _locationMonitor.OnChange(_ =>
        {
            InvalidateTimeZoneCache();
            InvalidateOverlayCache();
        });
        _optionsReload = _optionsMonitor.OnChange(_ => InvalidateOverlayCache());
    }

    public string Name => FrameFilterNames.OverlayText;

    public bool ShouldApply(CameraConfiguration configuration) => configuration.EnableImageOverlays;

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

    using var canvas = new SKCanvas(bitmap);
    DrawOverlay(canvas, bitmap.Width, bitmap.Height, stackResult, renderContext, cancellationToken);

        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyAsync(
        FilterFrame frame,
        FrameStackResult stackResult,
        CameraConfiguration configuration,
        FrameRenderContext? renderContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var canvas = frame.Surface.Canvas;
        var width = stackResult.StackedImage.Width;
        var height = stackResult.StackedImage.Height;

        if (width <= 0 || height <= 0)
        {
            var bounds = canvas.DeviceClipBounds;
            width = bounds.Width;
            height = bounds.Height;
        }

        DrawOverlay(canvas, width, height, stackResult, renderContext, cancellationToken);

        return ValueTask.CompletedTask;
    }

    private void InvalidateTimeZoneCache()
    {
        lock (_timeZoneSync)
        {
            _cachedTimeZone = null;
        }
        InvalidateOverlayCache();
    }

    private void DrawOverlay(
        SKCanvas canvas,
        int width,
        int height,
        FrameStackResult stackResult,
        FrameRenderContext? renderContext,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var location = _locationMonitor.CurrentValue;

        var latitude = renderContext?.LatitudeDeg ?? location.LatitudeDegrees;
        var longitude = renderContext?.LongitudeDeg ?? location.LongitudeDegrees;
        var timeZone = GetTimeZoneForLocation(latitude, longitude);

        var timestamp = renderContext?.Timestamp ?? stackResult.Timestamp;
        var localTimestamp = TimeZoneInfo.ConvertTime(timestamp, timeZone.TimeZone);

        var locationText = $"Lat: {FormatLatitude(latitude)} | Lon: {FormatLongitude(longitude)}";
        var timestampText = $"Local Time ({timeZone.DisplayId}): {localTimestamp.ToString(options.OverlayTextFormat)}";
        var exposureText = $"Exposure: {stackResult.Exposure.ExposureMilliseconds} ms | Gain: {stackResult.Exposure.Gain}";
        var integrationText = stackResult.FramesStacked > 1
            ? $"Integration: {stackResult.IntegrationMilliseconds} ms ({stackResult.FramesStacked} frames)"
            : null;

        string? rigText = null;
        if (renderContext is not null)
        {
            var rig = renderContext.Rig;
            var lens = rig.Lens;
            var lensLabel = string.IsNullOrWhiteSpace(lens.Name) ? lens.Model.ToString() : lens.Name;
            rigText = $"Rig: {rig.Name} | Lens: {lensLabel} ({lens.FocalLengthMm:0.0} mm)";
        }

        var lines = new List<OverlayLine>
        {
            new OverlayLine(locationText, true),
            new OverlayLine(timestampText, false),
            new OverlayLine(exposureText, false)
        };

        if (!string.IsNullOrWhiteSpace(rigText))
        {
            lines.Add(new OverlayLine(rigText!, false));
        }

        if (!string.IsNullOrWhiteSpace(integrationText))
        {
            lines.Add(new OverlayLine(integrationText!, false));
        }

        if (lines.Count == 0)
        {
            return;
        }

        var cachedOverlay = GetOrCreateOverlayImage(width, height, lines, options, cancellationToken);
        canvas.DrawImage(cachedOverlay.Image, cachedOverlay.DrawPoint);
    }

    private CachedTimeZone GetTimeZoneForLocation(double latitude, double longitude)
    {
        lock (_timeZoneSync)
        {
            if (_cachedTimeZone is { } cache && CoordinatesMatch(cache.Latitude, latitude) && CoordinatesMatch(cache.Longitude, longitude))
            {
                return cache;
            }

            string timeZoneId;

            try
            {
                var lookup = TimeZoneLookup.GetTimeZone(latitude, longitude);
                timeZoneId = string.IsNullOrWhiteSpace(lookup.Result) ? "UTC" : lookup.Result;
            }
            catch
            {
                timeZoneId = "UTC";
            }

            var timeZoneInfo = ResolveTimeZoneInfo(timeZoneId);
            var updated = new CachedTimeZone(latitude, longitude, timeZoneId, timeZoneInfo);
            _cachedTimeZone = updated;
            return updated;
        }
    }

    private static TimeZoneInfo ResolveTimeZoneInfo(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (TZConvert.TryIanaToWindows(timeZoneId, out var windowsId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }
        }
        catch (InvalidTimeZoneException)
        {
            // Fall through to UTC fallback.
        }

        return TimeZoneInfo.Utc;
    }

    private CachedOverlayImage GetOrCreateOverlayImage(
        int canvasWidth,
        int canvasHeight,
        IReadOnlyList<OverlayLine> lines,
        CameraPipelineOptions options,
        CancellationToken cancellationToken)
    {
        var fingerprint = ComputeFingerprint(canvasWidth, canvasHeight, lines, options);

        lock (_overlaySync)
        {
            if (_cachedOverlay is { } cached && cached.Fingerprint == fingerprint)
            {
                return cached;
            }
        }

        var rendered = RenderOverlayImage(canvasWidth, canvasHeight, lines, options, cancellationToken, fingerprint);

        lock (_overlaySync)
        {
            _cachedOverlay?.Dispose();
            _cachedOverlay = rendered;
            return _cachedOverlay;
        }
    }

    private CachedOverlayImage RenderOverlayImage(
        int canvasWidth,
        int canvasHeight,
        IReadOnlyList<OverlayLine> lines,
        CameraPipelineOptions options,
        CancellationToken cancellationToken,
        string fingerprint)
    {
        using var boldTypeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.Bold);
        using var regularTypeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.Normal);
        using var titleFont = new SKFont(boldTypeface, 24);
        using var subtitleFont = new SKFont(regularTypeface, 18);
        using var titlePaint = new SKPaint { IsAntialias = true, Color = new SKColor(173, 216, 230, 235) };
        using var subtitlePaint = new SKPaint { IsAntialias = true, Color = new SKColor(211, 211, 211, 230) };
        using var backgroundPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 160) };

        var measuredLines = MeasureLines(lines, titleFont, subtitleFont, titlePaint, subtitlePaint, cancellationToken);
        if (measuredLines.Count == 0)
        {
            throw new InvalidOperationException("OverlayTextFilter attempted to render without any lines.");
        }

        var margin = 18f;
        var lineSpacing = 6f;

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
    var rect = new SKRect(margin, canvasHeight - boxHeight - margin, margin + boxWidth, canvasHeight - margin);

        var overlayWidth = Math.Max(1, (int)Math.Ceiling(rect.Width));
        var overlayHeight = Math.Max(1, (int)Math.Ceiling(rect.Height));

        var info = new SKImageInfo(overlayWidth, overlayHeight, SKColorType.RgbaF16, SKAlphaType.Premul, LinearSrgbColorSpace);
        using var surface = SKSurface.Create(info) ?? throw new InvalidOperationException("Failed to allocate overlay surface.");
        var overlayCanvas = surface.Canvas;
        overlayCanvas.Clear(SKColors.Transparent);

        var localRect = new SKRect(0f, 0f, rect.Width, rect.Height);

        using (var path = new SKPath())
        {
            const float radius = 16f;
            path.AddRoundRect(localRect, radius, radius);
            overlayCanvas.DrawPath(path, backgroundPaint);
        }

        var baseline = margin;

        for (var i = 0; i < measuredLines.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = measuredLines[i];
            var paint = line.IsTitle ? titlePaint : subtitlePaint;
            var font = line.IsTitle ? titleFont : subtitleFont;

            var textBaseline = baseline - line.Metrics.Ascent;
            overlayCanvas.DrawText(line.Text, margin, textBaseline, SKTextAlign.Left, font, paint);

            baseline += line.Height;
            if (i < measuredLines.Count - 1)
            {
                baseline += lineSpacing;
            }
        }

        overlayCanvas.Flush();

        var image = surface.Snapshot();
        var drawPoint = new SKPoint(rect.Left, rect.Top);
        return new CachedOverlayImage(fingerprint, image, drawPoint);
    }

    private static string ComputeFingerprint(
        int canvasWidth,
        int canvasHeight,
        IReadOnlyList<OverlayLine> lines,
        CameraPipelineOptions options)
    {
        var hash = new HashCode();
        hash.Add(canvasWidth);
        hash.Add(canvasHeight);
        hash.Add(options.OverlayTextFormat, StringComparer.Ordinal);

        for (var i = 0; i < lines.Count; i++)
        {
            hash.Add(lines[i].IsTitle);
            hash.Add(lines[i].Text, StringComparer.Ordinal);
        }

        return hash.ToHashCode().ToString("X8", CultureInfo.InvariantCulture);
    }

    private void InvalidateOverlayCache()
    {
        lock (_overlaySync)
        {
            _cachedOverlay?.Dispose();
            _cachedOverlay = null;
        }
    }

    private static List<MeasuredLine> MeasureLines(
        IReadOnlyList<OverlayLine> lines,
        SKFont titleFont,
        SKFont bodyFont,
        SKPaint titlePaint,
        SKPaint bodyPaint,
        CancellationToken cancellationToken)
    {
        var measured = new List<MeasuredLine>(lines.Count);

        for (var i = 0; i < lines.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = lines[i];

            if (string.IsNullOrWhiteSpace(entry.Text))
            {
                continue;
            }

            var font = entry.IsTitle ? titleFont : bodyFont;
            var paint = entry.IsTitle ? titlePaint : bodyPaint;
            var width = font.MeasureText(entry.Text, paint);
            var metrics = font.Metrics;
            var height = metrics.Descent - metrics.Ascent;
            measured.Add(new MeasuredLine(entry.Text, entry.IsTitle, width, height, metrics));
        }

        return measured;
    }

    private sealed class CachedOverlayImage : IDisposable
    {
        public CachedOverlayImage(string fingerprint, SKImage image, SKPoint drawPoint)
        {
            Fingerprint = fingerprint;
            Image = image ?? throw new ArgumentNullException(nameof(image));
            DrawPoint = drawPoint;
        }

        public string Fingerprint { get; }
        public SKImage Image { get; }
        public SKPoint DrawPoint { get; }

        public void Dispose()
        {
            Image.Dispose();
        }
    }

    private readonly record struct OverlayLine(string Text, bool IsTitle);
    private readonly record struct MeasuredLine(string Text, bool IsTitle, float Width, float Height, SKFontMetrics Metrics);

    private static bool CoordinatesMatch(double a, double b)
        => Math.Abs(a - b) < 1e-6;

    private static string FormatLatitude(double value)
    {
        var hemisphere = value >= 0 ? 'N' : 'S';
        return $"{Math.Abs(value):F4}° {hemisphere}";
    }

    private static string FormatLongitude(double value)
    {
        var hemisphere = value >= 0 ? 'E' : 'W';
        return $"{Math.Abs(value):F4}° {hemisphere}";
    }

    private readonly record struct CachedTimeZone(double Latitude, double Longitude, string DisplayId, TimeZoneInfo TimeZone);

    public void Dispose()
    {
        _optionsReload?.Dispose();
        InvalidateOverlayCache();
    }
}
