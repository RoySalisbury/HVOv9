#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

/// <summary>
/// Draws constellation line figures by connecting the catalog stars for each configured constellation.
/// </summary>
public sealed class ConstellationFigureFilter : IFrameFilter
{
    private static readonly SKColor DefaultLineColor = new(127, 178, 255, 180);
    private static readonly float[] DashedPattern = { 8f, 6f };

    private readonly IOptionsMonitor<ConstellationFigureOptions> _optionsMonitor;
    private readonly IOptionsMonitor<StarCatalogOptions> _catalogMonitor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConstellationFigureFilter> _logger;

    private ConstellationFigureCache _cache;

    public ConstellationFigureFilter(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ConstellationFigureOptions> optionsMonitor,
        IOptionsMonitor<StarCatalogOptions> catalogMonitor,
        ILogger<ConstellationFigureFilter> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _catalogMonitor = catalogMonitor ?? throw new ArgumentNullException(nameof(catalogMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _cache = BuildCache(_optionsMonitor.CurrentValue);

        _optionsMonitor.OnChange(options => Volatile.Write(ref _cache, BuildCache(options)));
    }

    public string Name => FrameFilterNames.ConstellationFigures;

    public bool ShouldApply(CameraConfiguration configuration)
    {
        if (!configuration.EnableImageOverlays) return false;
        var cache = Volatile.Read(ref _cache);
        return cache.HasSegments;
    }

    public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
        => ApplyAsync(bitmap, stackResult, configuration, renderContext: null, cancellationToken);

    public async ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, FrameRenderContext? renderContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var engine = renderContext?.Engine;
        if (engine is null)
        {
            _logger.LogWarning("ConstellationFigures: no StarFieldEngine available; skipping overlay.");
            return;
        }

        var cache = Volatile.Read(ref _cache);
        if (!cache.HasSegments)
        {
            return;
        }

        var context = renderContext!;

        if (!double.IsFinite(context.LatitudeDeg) || !double.IsFinite(context.LongitudeDeg))
        {
            _logger.LogWarning(
                "Constellation figures: frame context missing valid coordinates (lat {Latitude}, lon {Longitude}); skipping overlay.",
                context.LatitudeDeg,
                context.LongitudeDeg);
            return;
        }

        var timestampUtc = context.Timestamp.UtcDateTime;
        if (timestampUtc == default)
        {
            timestampUtc = DateTime.UtcNow;
        }

        var catalogOptions = _catalogMonitor.CurrentValue;

        var visibleFigures = await ResolveVisibleConstellationsAsync(
            cache,
            context.LatitudeDeg,
            context.LongitudeDeg,
            timestampUtc,
            catalogOptions,
            engine,
            bitmap.Width,
            bitmap.Height,
            cancellationToken).ConfigureAwait(false);

        if (visibleFigures.Count == 0)
        {
            return;
        }

        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = cache.LineThickness,
            Color = cache.LineColor
        };

        using var pathEffect = cache.UseDashedLine ? SKPathEffect.CreateDash(DashedPattern, 0) : null;
        if (cache.UseDashedLine)
        {
            paint.PathEffect = pathEffect;
        }

        foreach (var figure in visibleFigures)
        {
            foreach (var segment in figure.Segments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!engine.ProjectStar(segment.Start, out var startX, out var startY))
                {
                    continue;
                }

                if (!engine.ProjectStar(segment.End, out var endX, out var endY))
                {
                    continue;
                }

                canvas.DrawLine(startX, startY, endX, endY, paint);
            }
        }

        return;
    }

    private ConstellationFigureCache BuildCache(ConstellationFigureOptions? options)
    {
        if (options is null)
        {
            return ConstellationFigureCache.Empty;
        }

        var colour = ResolveColor(options.LineColor, DefaultLineColor);
        var opacity = Math.Clamp(options.LineOpacity, 0.05f, 1.0f);
        var alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255.0), 16, 255);
        var lineColor = colour.WithAlpha(alpha);
        var thickness = Math.Clamp(options.LineThickness, 0.05f, 8.0f);

        using var scope = _scopeFactory.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IConstellationCatalog>();
        var figuresByCode = new Dictionary<string, ConstellationFigureSegments>(StringComparer.OrdinalIgnoreCase);
        var displayToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var figure in catalog.GetFigures())
        {
            var segments = BuildSegments(figure);
            if (segments.Count == 0)
            {
                continue;
            }

            var segmentsRecord = new ConstellationFigureSegments(figure.Abbreviation, figure.DisplayName, segments);
            figuresByCode[figure.Abbreviation] = segmentsRecord;
            displayToCode[figure.DisplayName] = figure.Abbreviation;
        }

        if (figuresByCode.Count == 0)
        {
            return ConstellationFigureCache.Empty;
        }

        var allowedFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var entry in options.Constellations ?? Array.Empty<string>())
        {
            var trimmed = entry?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (figuresByCode.ContainsKey(trimmed))
            {
                allowedFilters.Add(trimmed);
                allowedFilters.Add(figuresByCode[trimmed].DisplayName);
            }
            else if (displayToCode.TryGetValue(trimmed, out var code))
            {
                allowedFilters.Add(trimmed);
                allowedFilters.Add(code);
            }
            else
            {
                missing.Add(trimmed);
            }
        }

        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "Constellation figures: ignoring {Count} unknown constellation filter(s): {Names}.",
                missing.Count,
                string.Join(", ", missing));
        }

        return new ConstellationFigureCache(
            new ReadOnlyDictionary<string, ConstellationFigureSegments>(figuresByCode),
            allowedFilters,
            lineColor,
            thickness,
            options.UseDashedLine);
    }

    private static List<ConstellationSegment> BuildSegments(Data.ConstellationFigure figure)
    {
        var segments = new List<ConstellationSegment>();

        foreach (var line in figure.Lines)
        {
            if (line.Stars.Count < 2)
            {
                continue;
            }

            for (var i = 0; i < line.Stars.Count - 1; i++)
            {
                var current = line.Stars[i];
                var next = line.Stars[i + 1];

                segments.Add(new ConstellationSegment(current, next));
            }
        }

        return segments;
    }

    private static SKColor ResolveColor(string? hex, SKColor fallback)
        => !string.IsNullOrWhiteSpace(hex) && SKColor.TryParse(hex, out var parsed) ? parsed : fallback;

    private sealed record ConstellationSegment(Star Start, Star End);

    private sealed record ConstellationFigureSegments(string Abbreviation, string DisplayName, IReadOnlyList<ConstellationSegment> Segments);

    private async Task<IReadOnlyList<ConstellationFigureSegments>> ResolveVisibleConstellationsAsync(
        ConstellationFigureCache cache,
        double latitudeDeg,
        double longitudeDeg,
        DateTime timestampUtc,
        StarCatalogOptions catalogOptions,
        StarFieldEngine engine,
        int screenWidth,
        int screenHeight,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IStarRepository>();

        var result = await repository.GetVisibleByConstellationAsync(
            latitudeDeg,
            longitudeDeg,
            timestampUtc,
            magnitudeLimit: catalogOptions.MagnitudeLimit,
            minMaxAltitudeDeg: catalogOptions.MinMaxAltitudeDegrees,
            screenWidth,
            screenHeight,
            engine).ConfigureAwait(false);

        if (result.IsFailure)
        {
            if (result.Error is not OperationCanceledException)
            {
                _logger.LogError(result.Error, "Constellation figures: failed to resolve visible constellations.");
            }
            return Array.Empty<ConstellationFigureSegments>();
        }

        if (result.Value.Count == 0)
        {
            return Array.Empty<ConstellationFigureSegments>();
        }

        var visible = new List<ConstellationFigureSegments>(result.Value.Count);

        foreach (var constellation in result.Value)
        {
            if (!cache.FiguresByCode.TryGetValue(constellation.ConstellationCode, out var figure))
            {
                continue;
            }

            if (!cache.IsAllowed(figure))
            {
                continue;
            }

            visible.Add(figure);
        }

        return visible.Count == 0 ? Array.Empty<ConstellationFigureSegments>() : visible;
    }

    private sealed class ConstellationFigureCache
    {
        public static ConstellationFigureCache Empty { get; } = new(
            new ReadOnlyDictionary<string, ConstellationFigureSegments>(new Dictionary<string, ConstellationFigureSegments>(StringComparer.OrdinalIgnoreCase)),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            DefaultLineColor,
            1.0f,
            useDashedLine: false);

        public ConstellationFigureCache(
            IReadOnlyDictionary<string, ConstellationFigureSegments> figuresByCode,
            HashSet<string> allowedFilters,
            SKColor lineColor,
            float lineThickness,
            bool useDashedLine)
        {
            FiguresByCode = figuresByCode;
            AllowedFilters = allowedFilters ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            LineColor = lineColor;
            LineThickness = lineThickness;
            UseDashedLine = useDashedLine;
        }

        public IReadOnlyDictionary<string, ConstellationFigureSegments> FiguresByCode { get; }
        public HashSet<string> AllowedFilters { get; }
        public SKColor LineColor { get; }
        public float LineThickness { get; }
        public bool UseDashedLine { get; }
        public bool HasSegments => FiguresByCode.Count > 0;

        public bool IsAllowed(ConstellationFigureSegments figure)
        {
            if (AllowedFilters.Count == 0)
            {
                return true;
            }

            return AllowedFilters.Contains(figure.Abbreviation) || AllowedFilters.Contains(figure.DisplayName);
        }
    }
}
