#nullable enable

using System;
using System.Collections.Generic;
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

public sealed class OverlayTextFilter : IFrameFilter
{
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;
    private readonly IOptionsMonitor<ObservatoryLocationOptions> _locationMonitor;
    private readonly object _timeZoneSync = new();
    private CachedTimeZone? _cachedTimeZone;

    public OverlayTextFilter(
        IOptionsMonitor<CameraPipelineOptions> optionsMonitor,
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));

        _locationMonitor.OnChange(_ => InvalidateTimeZoneCache());
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

        var options = _optionsMonitor.CurrentValue;
        var location = _locationMonitor.CurrentValue;

        var latitude = renderContext?.LatitudeDeg ?? location.LatitudeDegrees;
        var longitude = renderContext?.LongitudeDeg ?? location.LongitudeDegrees;
        var timeZone = GetTimeZoneForLocation(latitude, longitude);

        var timestamp = renderContext?.Timestamp ?? stackResult.Timestamp;
        var localTimestamp = TimeZoneInfo.ConvertTime(timestamp, timeZone.TimeZone);

        using var canvas = new SKCanvas(bitmap);
        using var boldTypeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.Bold);
        using var regularTypeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.Normal);
        using var titleFont = new SKFont(boldTypeface, 24);
        using var subtitleFont = new SKFont(regularTypeface, 18);
        using var titlePaint = new SKPaint { IsAntialias = true, Color = new SKColor(173, 216, 230, 235) };
        using var subtitlePaint = new SKPaint { IsAntialias = true, Color = new SKColor(211, 211, 211, 230) };
        using var backgroundPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 160) };

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

        var titleMetrics = titleFont.Metrics;
        var subtitleMetrics = subtitleFont.Metrics;

        var lines = new List<(string Text, SKFont Font, SKPaint Paint, SKFontMetrics Metrics)>
        {
            (locationText, titleFont, titlePaint, titleMetrics),
            (timestampText, subtitleFont, subtitlePaint, subtitleMetrics),
            (exposureText, subtitleFont, subtitlePaint, subtitleMetrics)
        };

        if (!string.IsNullOrWhiteSpace(rigText))
        {
            lines.Add((rigText!, subtitleFont, subtitlePaint, subtitleMetrics));
        }

        if (!string.IsNullOrWhiteSpace(integrationText))
        {
            lines.Add((integrationText!, subtitleFont, subtitlePaint, subtitleMetrics));
        }

        var margin = 18f;
        var lineSpacing = 6f;

        var maxWidth = 0f;
        var contentHeight = 0f;

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metrics = line.Metrics;
            var height = metrics.Descent - metrics.Ascent;
            contentHeight += height;
            maxWidth = Math.Max(maxWidth, line.Font.MeasureText(line.Text, line.Paint));
        }

        contentHeight += lineSpacing * (lines.Count - 1);

        var boxWidth = maxWidth + margin * 2f;
        var boxHeight = contentHeight + margin * 2f;
        var rect = new SKRect(margin, bitmap.Height - boxHeight - margin, margin + boxWidth, bitmap.Height - margin);

        using (var path = new SKPath())
        {
            var radius = 16f;
            path.AddRoundRect(rect, radius, radius);
            canvas.DrawPath(path, backgroundPaint);
        }

        var baseline = rect.Top + margin - lines[0].Metrics.Ascent;

        for (var i = 0; i < lines.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = lines[i];
            canvas.DrawText(line.Text, rect.Left + margin, baseline, SKTextAlign.Left, line.Font, line.Paint);
            var height = line.Metrics.Descent - line.Metrics.Ascent;
            baseline += height;
            if (i < lines.Count - 1)
            {
                baseline += lineSpacing;
            }
        }

        return ValueTask.CompletedTask;
    }

    private void InvalidateTimeZoneCache()
    {
        lock (_timeZoneSync)
        {
            _cachedTimeZone = null;
        }
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
}
