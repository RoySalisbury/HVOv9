#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Simulation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

public sealed class CelestialAnnotationsFilter : IFrameFilter
{
    private const float DefaultStarRingRadius = 3.0f;
    private const float DefaultDeepSkyRingRadius = 4.0f;
    private const float DefaultPlanetRingRadius = 3.6f;

    private static readonly string SupportedPlanetNames = string.Join(", ", Enum.GetNames<PlanetBody>());

    private static readonly float[] DottedRingPattern = { 4f, 4f };

    private static readonly IReadOnlyDictionary<string, Star> CatalogStarIndex = BuildCatalogStarIndex();

    private static readonly SKColor DefaultStarColor = new(173, 216, 230, 230);
    private static readonly SKColor DefaultDeepSkyColor = new(202, 180, 255, 230);
    private static readonly SKColor DefaultStarLabelColor = new(235, 245, 255, 240);
    private static readonly SKColor DefaultPlanetLabelColor = new(255, 226, 194, 240);
    private static readonly SKColor DefaultDeepSkyLabelColor = new(240, 228, 255, 240);

    private readonly IOptionsMonitor<ObservatoryLocationOptions> _locationMonitor;
    private readonly IOptionsMonitor<StarCatalogOptions> _catalogMonitor;
    private readonly IOptionsMonitor<CelestialAnnotationsOptions> _annotationMonitor;
    private readonly IOptionsMonitor<CardinalDirectionsOptions> _cardinalMonitor;
    private readonly ILogger<CelestialAnnotationsFilter> _logger;
    private readonly object _planetWarningLock = new();

    private readonly HashSet<PlanetBody> _suppressedPlanetWarnings = new();

    private AnnotationCache _cache;

    public CelestialAnnotationsFilter(
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor,
        IOptionsMonitor<StarCatalogOptions> catalogMonitor,
        IOptionsMonitor<CelestialAnnotationsOptions> annotationMonitor,
        IOptionsMonitor<CardinalDirectionsOptions> cardinalMonitor,
        ILogger<CelestialAnnotationsFilter> logger)
    {
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));
        _catalogMonitor = catalogMonitor ?? throw new ArgumentNullException(nameof(catalogMonitor));
        _annotationMonitor = annotationMonitor ?? throw new ArgumentNullException(nameof(annotationMonitor));
        _cardinalMonitor = cardinalMonitor ?? throw new ArgumentNullException(nameof(cardinalMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _cache = BuildCache(annotationMonitor.CurrentValue);

        _annotationMonitor.OnChange(options =>
        {
            var newCache = BuildCache(options);
            Volatile.Write(ref _cache, newCache);

            lock (_planetWarningLock)
            {
                _suppressedPlanetWarnings.Clear();
            }
        });
    }

    public string Name => FrameFilterNames.CelestialAnnotations;

    public bool ShouldApply(CameraConfiguration configuration)
    {
        if (!configuration.EnableImageOverlays)
        {
            return false;
        }

        var cache = Volatile.Read(ref _cache);
        if (cache.IsEmpty)
        {
            return false;
        }

        var catalogOptions = _catalogMonitor.CurrentValue;
        return cache.StarTargets.Count > 0
            || cache.DeepSkyTargets.Count > 0
            || ShouldAnnotatePlanets(catalogOptions, cache);
    }

    public ValueTask ApplyAsync(
        SKBitmap bitmap,
        FrameStackResult stackResult,
        CameraConfiguration configuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cache = Volatile.Read(ref _cache);
        var catalogOptions = _catalogMonitor.CurrentValue;
        var annotatePlanets = ShouldAnnotatePlanets(catalogOptions, cache);

        if (cache.StarTargets.Count == 0 && cache.DeepSkyTargets.Count == 0 && !annotatePlanets)
        {
            return ValueTask.CompletedTask;
        }

        var location = _locationMonitor.CurrentValue;

        var frameTimestampUtc = stackResult.Frame.Timestamp.UtcDateTime;
        if (frameTimestampUtc == default)
        {
            frameTimestampUtc = DateTime.UtcNow;
        }

        var labelFontSize = Math.Max(4f, cache.LabelFontSize);

        using var canvas = new SKCanvas(bitmap);
        using var textTypeface = PipelineFontUtilities.ResolveTypeface(SKFontStyleWeight.Normal);
        using var textFont = new SKFont(textTypeface, labelFontSize);
        using var labelPaint = new SKPaint { IsAntialias = true, Color = cache.StarLabelColor };
        using var haloPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 150) };

        var flipHorizontal = _cardinalMonitor.CurrentValue.SwapEastWest;
        var engine = new StarFieldEngine(
            bitmap.Width,
            bitmap.Height,
            location.LatitudeDegrees,
            location.LongitudeDegrees,
            stackResult.Frame.Timestamp.UtcDateTime,   // render at the frame time
            MockFisheyeCameraAdapter.DefaultProjection,
            MockFisheyeCameraAdapter.DefaultHorizonPadding,
            flipHorizontal: _cardinalMonitor.CurrentValue.SwapEastWest,
            fovDeg: MockFisheyeCameraAdapter.DefaultFovDeg,
            applyRefraction: true);


        AnnotateTargets(cache.StarTargets, engine, canvas, haloPaint, textFont, labelPaint, bitmap.Width, bitmap.Height, cancellationToken);
        AnnotateTargets(cache.DeepSkyTargets, engine, canvas, haloPaint, textFont, labelPaint, bitmap.Width, bitmap.Height, cancellationToken);

        if (annotatePlanets)
        {
            var planetMarks = ComputePlanetMarks(location, catalogOptions, frameTimestampUtc, cache.PlanetBodyLookup);
            if (planetMarks.Count > 0)
            {
                foreach (var planet in planetMarks)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!engine.ProjectStar(planet.Star, out var px, out var py))
                    {
                        continue;
                    }

                    var position = new SKPoint(px, py);
                    DrawMarkerWithLabel(
                        canvas,
                        position,
                        planet.Name,
                        planet.Color,
                        cache.PlanetRingRadius,
                        textFont,
                        labelPaint,
                        haloPaint,
                        bitmap.Width,
                        bitmap.Height,
                        cache.PlanetLabelColor,
                        useFaintRing: false);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private AnnotationCache BuildCache(CelestialAnnotationsOptions options)
    {
        if (options is null)
        {
            return AnnotationCache.Empty;
        }

        var starRingRadius = Math.Clamp(options.StarRingRadius, 1.0f, 64.0f);
        var planetRingRadius = Math.Clamp(options.PlanetRingRadius, 1.0f, 64.0f);
        var deepSkyRingRadius = Math.Clamp(options.DeepSkyRingRadius, 1.0f, 64.0f);

        var starLabelColor = ResolveColor(options.StarLabelColor, DefaultStarLabelColor);
        var planetLabelColor = ResolveColor(options.PlanetLabelColor, DefaultPlanetLabelColor);
        var deepSkyLabelColor = ResolveColor(options.DeepSkyLabelColor, DefaultDeepSkyLabelColor);

        var starTargets = new List<ResolvedAnnotation>();
        var missingStars = new List<string>();

        foreach (var entry in options.StarNames ?? Array.Empty<string>())
        {
            var name = entry?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (CatalogStarIndex.TryGetValue(name, out var star))
            {
                starTargets.Add(new ResolvedAnnotation(name, star, DefaultStarColor, starRingRadius, UseFaintRing: false, starLabelColor));
            }
            else
            {
                missingStars.Add(name);
            }
        }

        if (missingStars.Count > 0)
        {
            _logger.LogWarning(
                "Celestial annotations: unable to resolve {Count} star name(s): {Names}. Provide RA/Dec via DeepSkyObjects when necessary.",
                missingStars.Count,
                string.Join(", ", missingStars));
        }

        var deepSkyTargets = new List<ResolvedAnnotation>();

        foreach (var obj in options.DeepSkyObjects ?? Array.Empty<DeepSkyObjectOption>())
        {
            if (string.IsNullOrWhiteSpace(obj.Name))
            {
                continue;
            }

            var color = ResolveColor(obj.Color, DefaultDeepSkyColor);
            var star = new Star(obj.RightAscensionHours, obj.DeclinationDegrees, obj.Magnitude, color);
            deepSkyTargets.Add(new ResolvedAnnotation(obj.Name, star, color, deepSkyRingRadius, UseFaintRing: true, deepSkyLabelColor));
        }

        var planetBodies = new HashSet<PlanetBody>();
        var invalidPlanets = new List<string>();

        foreach (var entry in options.PlanetNames ?? Array.Empty<string>())
        {
            var name = entry?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (Enum.TryParse<PlanetBody>(name, ignoreCase: true, out var body))
            {
                planetBodies.Add(body);
            }
            else
            {
                invalidPlanets.Add(name);
            }
        }

        if (invalidPlanets.Count > 0)
        {
            _logger.LogWarning(
                "Celestial annotations: ignoring unsupported planet entries: {Names}. Supported bodies: {Supported}.",
                string.Join(", ", invalidPlanets),
                SupportedPlanetNames);
        }

        _logger.LogDebug(
            "Celestial annotations refreshed with {StarCount} star(s), {PlanetCount} planet(s), and {DsoCount} deep-sky object(s).",
            starTargets.Count,
            planetBodies.Count,
            deepSkyTargets.Count);

        var labelFontSize = Math.Clamp(options.LabelFontSize, 4.0f, 72.0f);

        return new AnnotationCache(
            starTargets.Count > 0 ? starTargets : Array.Empty<ResolvedAnnotation>(),
            deepSkyTargets.Count > 0 ? deepSkyTargets : Array.Empty<ResolvedAnnotation>(),
            planetBodies,
            labelFontSize,
            starLabelColor,
            planetLabelColor,
            deepSkyLabelColor,
            planetRingRadius,
            starRingRadius,
            deepSkyRingRadius);
    }

    private static void AnnotateTargets(
        IReadOnlyList<ResolvedAnnotation> targets,
        StarFieldEngine engine,
        SKCanvas canvas,
        SKPaint haloPaint,
        SKFont textFont,
        SKPaint labelPaint,
        int canvasWidth,
        int canvasHeight,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return;
        }

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!engine.ProjectStar(target.Star, out var px, out var py))
            {
                continue;
            }

            var position = new SKPoint(px, py);
            DrawMarkerWithLabel(
                canvas,
                position,
                target.Name,
                target.RingColor,
                target.RingRadius,
                textFont,
                labelPaint,
                haloPaint,
                canvasWidth,
                canvasHeight,
                target.LabelColor,
                target.UseFaintRing);
        }
    }

    private IReadOnlyList<PlanetMark> ComputePlanetMarks(
        ObservatoryLocationOptions location,
        StarCatalogOptions options,
        DateTime timestampUtc,
        HashSet<PlanetBody> requestedBodies)
    {
        if (requestedBodies.Count == 0)
        {
            return Array.Empty<PlanetMark>();
        }

        var marks = PlanetMarks.Compute(
            latitudeDeg: location.LatitudeDegrees,
            longitudeDeg: location.LongitudeDegrees,
            utc: timestampUtc,
            includeUranusNeptune: options.IncludeOuterPlanets,
            includeSun: options.IncludeSun);

        if (marks.Count == 0)
        {
            return Array.Empty<PlanetMark>();
        }

        var filtered = new List<PlanetMark>(marks.Count);

        foreach (var mark in marks)
        {
            if (!requestedBodies.Contains(mark.Body))
            {
                continue;
            }

            if (!ShouldIncludePlanet(mark.Body, options))
            {
                LogPlanetSuppressed(mark.Body);
                continue;
            }

            filtered.Add(mark);
        }

        foreach (var body in requestedBodies)
        {
            if (!ShouldIncludePlanet(body, options))
            {
                LogPlanetSuppressed(body);
            }
        }

        return filtered.Count == 0 ? Array.Empty<PlanetMark>() : filtered;
    }

    private void LogPlanetSuppressed(PlanetBody body)
    {
        lock (_planetWarningLock)
        {
            if (_suppressedPlanetWarnings.Add(body))
            {
                _logger.LogInformation(
                    "Celestial annotations: planet {Planet} is configured for labeling but is disabled by the current star catalog options.",
                    body);
            }
        }
    }

    private static bool ShouldAnnotatePlanets(StarCatalogOptions options, AnnotationCache cache)
        => cache.PlanetBodyLookup.Count > 0 && options.AnnotatePlanets && ShouldComputePlanets(options);

    private static bool ShouldComputePlanets(StarCatalogOptions options)
        => options.IncludePlanets || options.IncludeMoon || options.IncludeOuterPlanets || options.IncludeSun;

    private static bool ShouldIncludePlanet(PlanetBody body, StarCatalogOptions options) => body switch
    {
        PlanetBody.Moon => options.IncludeMoon,
        PlanetBody.Sun => options.IncludeSun,
        PlanetBody.Uranus or PlanetBody.Neptune => options.IncludeOuterPlanets,
        _ => options.IncludePlanets
    };

    private static void DrawMarkerWithLabel(
        SKCanvas canvas,
        SKPoint position,
        string label,
        SKColor ringColor,
        float ringRadius,
        SKFont textFont,
        SKPaint labelPaint,
        SKPaint haloPaint,
        int canvasWidth,
        int canvasHeight,
        SKColor labelColor,
        bool useFaintRing)
    {
        var alphaScale = useFaintRing ? 0.35f : 0.65f;
        var alpha = (byte)Math.Clamp((int)(ringColor.Alpha * alphaScale), 24, 220);
        var adjustedRingColor = ringColor.WithAlpha(alpha);

        using var ringPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = adjustedRingColor,
            StrokeWidth = useFaintRing ? 1.0f : 1.2f,
            PathEffect = SKPathEffect.CreateDash(DottedRingPattern, 0)
        };

        canvas.DrawCircle(position, ringRadius, ringPaint);

        const float padding = 6f;
        const float horizontalOffset = 12f;

        var previousLabelColor = labelPaint.Color;
        labelPaint.Color = labelColor;

        var textWidth = textFont.MeasureText(label, labelPaint);
        var textHeight = textFont.Metrics.CapHeight;

        var boxRect = new SKRect(
            position.X + horizontalOffset,
            position.Y - ringRadius - (textHeight + padding * 2f),
            position.X + horizontalOffset + textWidth + padding * 2f,
            position.Y - ringRadius);

        ClampRectToCanvas(ref boxRect, canvasWidth, canvasHeight);

        canvas.DrawRoundRect(boxRect, 5f, 5f, haloPaint);

        var textBaseline = boxRect.Bottom - padding;
        var textX = boxRect.Left + padding;
        canvas.DrawText(label, textX, textBaseline, SKTextAlign.Left, textFont, labelPaint);

        labelPaint.Color = previousLabelColor;
    }

    private static SKColor ResolveColor(string? hex, SKColor fallback)
        => !string.IsNullOrWhiteSpace(hex) && SKColor.TryParse(hex, out var parsed) ? parsed : fallback;

    private static IReadOnlyDictionary<string, Star> BuildCatalogStarIndex()
    {
        var index = new Dictionary<string, Star>(StringComparer.OrdinalIgnoreCase);

        foreach (var constellation in ConstellationCatalog.All.Values)
        {
            foreach (var star in constellation)
            {
                if (!index.ContainsKey(star.Name))
                {
                    index[star.Name] = star.Star;
                }
            }
        }

        return index;
    }

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

    private sealed record ResolvedAnnotation(string Name, Star Star, SKColor RingColor, float RingRadius, bool UseFaintRing, SKColor LabelColor);

    private sealed class AnnotationCache
    {
        public static AnnotationCache Empty { get; } = new(
            Array.Empty<ResolvedAnnotation>(),
            Array.Empty<ResolvedAnnotation>(),
            new HashSet<PlanetBody>(),
            8.0f,
            DefaultStarLabelColor,
            DefaultPlanetLabelColor,
            DefaultDeepSkyLabelColor,
            DefaultPlanetRingRadius,
            DefaultStarRingRadius,
            DefaultDeepSkyRingRadius);

        public AnnotationCache(
            IReadOnlyList<ResolvedAnnotation> starTargets,
            IReadOnlyList<ResolvedAnnotation> deepSkyTargets,
            HashSet<PlanetBody> planetBodies,
            float labelFontSize,
            SKColor starLabelColor,
            SKColor planetLabelColor,
            SKColor deepSkyLabelColor,
            float planetRingRadius,
            float starRingRadius,
            float deepSkyRingRadius)
        {
            StarTargets = starTargets;
            DeepSkyTargets = deepSkyTargets;
            PlanetBodyLookup = planetBodies;
            LabelFontSize = labelFontSize;
            StarLabelColor = starLabelColor;
            PlanetLabelColor = planetLabelColor;
            DeepSkyLabelColor = deepSkyLabelColor;
            PlanetRingRadius = planetRingRadius;
            StarRingRadius = starRingRadius;
            DeepSkyRingRadius = deepSkyRingRadius;
        }

        public IReadOnlyList<ResolvedAnnotation> StarTargets { get; }

        public IReadOnlyList<ResolvedAnnotation> DeepSkyTargets { get; }

        public HashSet<PlanetBody> PlanetBodyLookup { get; }

        public float LabelFontSize { get; }

        public SKColor StarLabelColor { get; }

        public SKColor PlanetLabelColor { get; }

        public SKColor DeepSkyLabelColor { get; }

        public float PlanetRingRadius { get; }

        public float StarRingRadius { get; }

        public float DeepSkyRingRadius { get; }

        public bool IsEmpty => StarTargets.Count == 0 && DeepSkyTargets.Count == 0 && PlanetBodyLookup.Count == 0;
    }
}
