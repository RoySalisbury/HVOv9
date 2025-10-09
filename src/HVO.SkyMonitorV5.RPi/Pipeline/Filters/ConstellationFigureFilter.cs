#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
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
    private readonly IConstellationCatalog _constellationCatalog;
    private readonly ILogger<ConstellationFigureFilter> _logger;

    private ConstellationFigureCache _cache;

    public ConstellationFigureFilter(
        IConstellationCatalog constellationCatalog,
        IOptionsMonitor<ConstellationFigureOptions> optionsMonitor,
        ILogger<ConstellationFigureFilter> logger)
    {
        _constellationCatalog = constellationCatalog ?? throw new ArgumentNullException(nameof(constellationCatalog));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
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

    public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, FrameRenderContext? renderContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var engine = renderContext?.Engine;
        if (engine is null)
        {
            _logger.LogWarning("ConstellationFigures: no StarFieldEngine available; skipping overlay.");
            return ValueTask.CompletedTask;
        }

        var cache = Volatile.Read(ref _cache);
        if (!cache.HasSegments)
        {
            return ValueTask.CompletedTask;
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

        foreach (var figure in cache.Figures)
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

        return ValueTask.CompletedTask;
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

        var requested = options.Constellations ?? Array.Empty<string>();
        var figuresByName = _constellationCatalog
            .GetFigures()
            .ToDictionary(figure => figure.DisplayName, StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> names;
        if (requested.Count == 0)
        {
            names = figuresByName.Keys;
        }
        else
        {
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var normalized = new List<string>(requested.Count);
            foreach (var entry in requested)
            {
                var trimmed = entry?.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                if (unique.Add(trimmed))
                {
                    normalized.Add(trimmed);
                }
            }

            names = normalized;
        }

        var figures = new List<ConstellationFigureSegments>();
        var missing = new List<string>();

        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!figuresByName.TryGetValue(name, out var figure))
            {
                missing.Add(name);
                continue;
            }

            var segments = BuildSegments(figure);
            if (segments.Count == 0)
            {
                continue;
            }

            figures.Add(new ConstellationFigureSegments(name, segments));
        }

        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "Constellation figures: unable to resolve {Count} constellation(s): {Names}.",
                missing.Count,
                string.Join(", ", missing));
        }

        return figures.Count == 0
            ? ConstellationFigureCache.Empty
            : new ConstellationFigureCache(figures, lineColor, thickness, options.UseDashedLine);
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

    private sealed record ConstellationFigureSegments(string Name, IReadOnlyList<ConstellationSegment> Segments);

    private sealed class ConstellationFigureCache
    {
        public static ConstellationFigureCache Empty { get; } = new(
            Array.Empty<ConstellationFigureSegments>(),
            DefaultLineColor,
            1.0f,
            useDashedLine: false);

        public ConstellationFigureCache(
            IReadOnlyList<ConstellationFigureSegments> figures,
            SKColor lineColor,
            float lineThickness,
            bool useDashedLine)
        {
            Figures = figures;
            LineColor = lineColor;
            LineThickness = lineThickness;
            UseDashedLine = useDashedLine;
        }

    public IReadOnlyList<ConstellationFigureSegments> Figures { get; }
        public SKColor LineColor { get; }
        public float LineThickness { get; }
        public bool UseDashedLine { get; }
        public bool HasSegments => Figures.Count > 0;
    }
}
