#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

/// <summary>
/// Draws star / DSO rings and planet labels using the same StarFieldEngine instance
/// that rendered the frame (supplied via FrameRenderContext).
/// </summary>
public sealed class CelestialAnnotationsFilter : IFrameFilter
{
    private const float DefaultStarRingRadius = 3.0f;
    private const float DefaultDeepSkyRingRadius = 4.0f;
    private const float DefaultPlanetRingRadius = 3.6f;

    private static readonly string SupportedPlanetNames = string.Join(", ", Enum.GetNames<PlanetBody>());
    private static readonly float[] DottedRingPattern = { 4f, 4f };

    private static readonly SKColor DefaultStarColor = new(173, 216, 230, 230);
    private static readonly SKColor DefaultDeepSkyColor = new(202, 180, 255, 230);
    private static readonly SKColor DefaultStarLabelColor = new(235, 245, 255, 240);
    private static readonly SKColor DefaultPlanetLabelColor = new(255, 226, 194, 240);
    private static readonly SKColor DefaultDeepSkyLabelColor = new(240, 228, 255, 240);

    private readonly IOptionsMonitor<ObservatoryLocationOptions> _locationMonitor;
    private readonly IOptionsMonitor<StarCatalogOptions> _catalogMonitor;
    private readonly IOptionsMonitor<CelestialAnnotationsOptions> _annotationMonitor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CelestialAnnotationsFilter> _logger;

    private readonly object _planetWarningLock = new();
    private readonly HashSet<PlanetBody> _suppressedPlanetWarnings = new();

    private readonly IReadOnlyDictionary<string, Star> _catalogStarIndex;
    private AnnotationCache _cache;

    public CelestialAnnotationsFilter(
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor,
        IOptionsMonitor<StarCatalogOptions> catalogMonitor,
        IOptionsMonitor<CelestialAnnotationsOptions> annotationMonitor,
        IServiceScopeFactory scopeFactory,
        ILogger<CelestialAnnotationsFilter> logger)
    {
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));
        _catalogMonitor = catalogMonitor ?? throw new ArgumentNullException(nameof(catalogMonitor));
        _annotationMonitor = annotationMonitor ?? throw new ArgumentNullException(nameof(annotationMonitor));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        using var scope = _scopeFactory.CreateScope();
        var constellationCatalog = scope.ServiceProvider.GetRequiredService<IConstellationCatalog>();
        _catalogStarIndex = BuildCatalogStarIndex(constellationCatalog);
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
        if (!configuration.EnableImageOverlays) return false;
        var cache = Volatile.Read(ref _cache);
        var catalogOptions = _catalogMonitor.CurrentValue;

        var hasStarAnnotations = cache.StarTargets.Count > 0 || cache.AutoStarSelection.Enabled;

        return hasStarAnnotations
            || cache.DeepSkyTargets.Count > 0
            || ShouldAnnotatePlanets(catalogOptions, cache);
    }

    public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
        => ApplyAsync(bitmap, stackResult, configuration, renderContext: null, cancellationToken);

    public async ValueTask ApplyAsync(
        SKBitmap bitmap,
        FrameStackResult stackResult,
        CameraConfiguration configuration,
        FrameRenderContext? renderContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var engine = renderContext?.Engine;
        if (engine is null)
        {
            _logger.LogWarning("CelestialAnnotations: no StarFieldEngine available; skipping overlay.");
            return;
        }

        var context = renderContext!;

        var cache = Volatile.Read(ref _cache);
        var catalogOptions = _catalogMonitor.CurrentValue;
        var annotatePlanets = ShouldAnnotatePlanets(catalogOptions, cache);

        IReadOnlyList<ResolvedAnnotation> starAnnotations = cache.StarTargets;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Celestial annotations: starting with {ManualCount} manual star target(s); auto-selection enabled: {AutoEnabled} (count {AutoCount}, mag ≤ {AutoLimit:F1}).",
                starAnnotations.Count,
                cache.AutoStarSelection.Enabled,
                cache.AutoStarSelection.Count,
                cache.AutoStarSelection.MagnitudeLimit);
        }

        if (cache.AutoStarSelection.Enabled)
        {
            if (renderContext is null)
            {
                _logger.LogWarning("CelestialAnnotations: automatic star selection enabled but no render context provided; skipping auto stars.");
            }
            else
            {
                var autoAnnotations = await ResolveAutomaticStarAnnotationsAsync(cache, renderContext, catalogOptions, cancellationToken).ConfigureAwait(false);

                if (autoAnnotations.Count > 0)
                {
                    if (starAnnotations.Count == 0)
                    {
                        starAnnotations = autoAnnotations;
                    }
                    else
                    {
                        var merged = new List<ResolvedAnnotation>(starAnnotations);
                        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var annotation in starAnnotations)
                        {
                            seen.Add(annotation.Name);
                        }

                        foreach (var annotation in autoAnnotations)
                        {
                            if (seen.Add(annotation.Name))
                            {
                                merged.Add(annotation);
                            }
                        }

                        starAnnotations = merged;
                    }

                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Celestial annotations: merged {AutoCount} automatically-selected star(s) with manual targets; total now {Total}.",
                            autoAnnotations.Count,
                            starAnnotations.Count);
                    }
                }
                else
                {
                    _logger.LogDebug("Celestial annotations: automatic star selection returned 0 candidates after filtering.");
                }
            }
        }

        if (starAnnotations.Count > 0 && _logger.IsEnabled(LogLevel.Debug))
        {
            var summaries = string.Join(
                ", ",
                starAnnotations.Select(annotation =>
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} (mag {1:F1})",
                        annotation.Name,
                        annotation.Star.Magnitude)));

            _logger.LogDebug(
                "Celestial annotations: labeling {StarCount} star(s): {Stars}.",
                starAnnotations.Count,
                summaries);
        }

        if (starAnnotations.Count == 0 && cache.DeepSkyTargets.Count == 0 && !annotatePlanets)
        {
            _logger.LogDebug(
                "Celestial annotations: skipping overlay; no stars, deep sky objects, or planets resolved (auto-enabled {AutoEnabled}).",
                cache.AutoStarSelection.Enabled);
            return;
        }

        var location = _locationMonitor.CurrentValue;

        var frameTimestampUtc = context.Timestamp.UtcDateTime;
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

        var renderedStars = AnnotateTargets(
            "star",
            starAnnotations,
            engine,
            canvas,
            haloPaint,
            textFont,
            labelPaint,
            bitmap.Width,
            bitmap.Height,
            cancellationToken,
            _logger);

        var renderedDeepSky = AnnotateTargets(
            "deep-sky",
            cache.DeepSkyTargets,
            engine,
            canvas,
            haloPaint,
            textFont,
            labelPaint,
            bitmap.Width,
            bitmap.Height,
            cancellationToken,
            _logger);

        if (starAnnotations.Count > 0 && renderedStars < starAnnotations.Count)
        {
            _logger.LogWarning(
                "Celestial annotations: rendered {Rendered} of {Requested} star annotations (skipped {Skipped}).",
                renderedStars,
                starAnnotations.Count,
                starAnnotations.Count - renderedStars);
        }

        if (cache.DeepSkyTargets.Count > 0 && renderedDeepSky < cache.DeepSkyTargets.Count)
        {
            _logger.LogWarning(
                "Celestial annotations: rendered {Rendered} of {Requested} deep-sky annotations (skipped {Skipped}).",
                renderedDeepSky,
                cache.DeepSkyTargets.Count,
                cache.DeepSkyTargets.Count - renderedDeepSky);
        }

        if (annotatePlanets)
        {
            var planetMarks = await ComputePlanetMarks(location, catalogOptions, frameTimestampUtc, cache.PlanetBodyLookup, cancellationToken)
                .ConfigureAwait(false);

            foreach (var planet in planetMarks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!engine.ProjectStar(planet.Star, out var px, out var py))
                    continue;

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

    // ------- helpers (unchanged from your previous version, trimmed where possible) -------

    private AnnotationCache BuildCache(CelestialAnnotationsOptions options)
    {
        if (options is null) return AnnotationCache.Empty;

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
            if (string.IsNullOrEmpty(name)) continue;

            if (_catalogStarIndex.TryGetValue(name, out var star))
                starTargets.Add(new ResolvedAnnotation(name, star, DefaultStarColor, starRingRadius, UseFaintRing: false, starLabelColor));
            else
                missingStars.Add(name);
        }

        if (missingStars.Count > 0)
            _logger.LogWarning("Celestial annotations: unable to resolve {Count} star name(s): {Names}.",
                missingStars.Count, string.Join(", ", missingStars));

        var deepSkyTargets = new List<ResolvedAnnotation>();
        foreach (var obj in options.DeepSkyObjects ?? Array.Empty<DeepSkyObjectOption>())
        {
            if (string.IsNullOrWhiteSpace(obj.Name)) continue;
            var color = ResolveColor(obj.Color, DefaultDeepSkyColor);
            var star = new Star(obj.RightAscensionHours, obj.DeclinationDegrees, obj.Magnitude, color);
            deepSkyTargets.Add(new ResolvedAnnotation(obj.Name, star, color, deepSkyRingRadius, UseFaintRing: true, deepSkyLabelColor));
        }

        var planetBodies = new HashSet<PlanetBody>();
        var invalidPlanets = new List<string>();
        var configuredPlanets = options.PlanetNames;
        if (configuredPlanets is null || configuredPlanets.Count == 0)
        {
            foreach (var body in Enum.GetValues<PlanetBody>())
            {
                planetBodies.Add(body);
            }
        }
        else
        {
            foreach (var entry in configuredPlanets)
            {
                var name = entry?.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                if (Enum.TryParse<PlanetBody>(name, ignoreCase: true, out var body))
                    planetBodies.Add(body);
                else
                    invalidPlanets.Add(name);
            }
        }

        if (invalidPlanets.Count > 0)
            _logger.LogWarning("Celestial annotations: ignoring unsupported planet entries: {Names}. Supported bodies: {Supported}.",
                string.Join(", ", invalidPlanets), SupportedPlanetNames);

        var labelFontSize = Math.Clamp(options.LabelFontSize, 4.0f, 72.0f);

        var autoCount = Math.Clamp(options.AutoStarCount, 0, 200);
        var autoMagnitude = Math.Clamp(options.AutoStarMagnitudeLimit, -2.0, 15.0);
        var autoSelection = new AutoStarSelectionSettings(
            options.UseAutomaticStarSelection && autoCount > 0,
            autoCount,
            autoMagnitude);

        return new AnnotationCache(
            starTargets.Count > 0 ? starTargets : Array.Empty<ResolvedAnnotation>(),
            deepSkyTargets.Count > 0 ? deepSkyTargets : Array.Empty<ResolvedAnnotation>(),
            planetBodies,
            options.AnnotatePlanets,
            labelFontSize,
            starLabelColor,
            planetLabelColor,
            deepSkyLabelColor,
            planetRingRadius,
            starRingRadius,
            deepSkyRingRadius,
            autoSelection);
    }

    private static int AnnotateTargets(
        string targetType,
        IReadOnlyList<ResolvedAnnotation> targets,
        StarFieldEngine engine,
        SKCanvas canvas,
        SKPaint haloPaint,
        SKFont textFont,
        SKPaint labelPaint,
        int canvasWidth,
        int canvasHeight,
        CancellationToken cancellationToken,
        ILogger logger)
    {
        if (targets.Count == 0)
        {
            return 0;
        }

        var projectedCount = 0;
        var skippedCount = 0;
        var sampleCoordinates = new List<string>(Math.Min(3, targets.Count));

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!engine.ProjectStar(target.Star, out var px, out var py))
            {
                skippedCount++;
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

            projectedCount++;

            if (sampleCoordinates.Count < 3)
            {
                sampleCoordinates.Add(string.Format(CultureInfo.InvariantCulture, "{0}: ({1:F1}, {2:F1})", target.Name, position.X, position.Y));
            }
        }

        if (projectedCount == 0)
        {
            logger.LogWarning(
                "Celestial annotations: unable to project any {TargetType} annotation(s) onto the canvas (attempted {Attempts}).",
                targetType,
                targets.Count);
        }
        else if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Celestial annotations: projected {Projected}/{Total} {TargetType} annotation(s); skipped {Skipped} (sample: {Sample}).",
                projectedCount,
                targets.Count,
                targetType,
                skippedCount,
                sampleCoordinates.Count > 0 ? string.Join(", ", sampleCoordinates) : "n/a");
        }

        return projectedCount;
    }

    private async Task<IReadOnlyList<ResolvedAnnotation>> ResolveAutomaticStarAnnotationsAsync(
        AnnotationCache cache,
        FrameRenderContext context,
        StarCatalogOptions catalogOptions,
        CancellationToken cancellationToken)
    {
        var settings = cache.AutoStarSelection;
        if (!settings.Enabled)
        {
            return Array.Empty<ResolvedAnnotation>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IStarRepository>();

        var effectiveMagnitude = Math.Min(settings.MagnitudeLimit, catalogOptions.MagnitudeLimit);
        var desiredCount = Math.Max(settings.Count, 1);
        var fetchCount = Math.Min(Math.Max(desiredCount * 3, desiredCount), Math.Max(catalogOptions.TopStarCount, desiredCount));

        var timestampUtc = context.Timestamp.UtcDateTime;
        if (timestampUtc == default)
        {
            timestampUtc = DateTime.UtcNow;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Celestial annotations: requesting visible stars (lat {Lat:F3}, lon {Lon:F3}, mag ≤ {MagLimit:F1}, desired {Desired}, fetch {Fetch}, ra bins {RaBins}, dec bands {DecBands}).",
                context.LatitudeDeg,
                context.LongitudeDeg,
                effectiveMagnitude,
                desiredCount,
                fetchCount,
                catalogOptions.RightAscensionBins,
                catalogOptions.DeclinationBands);
        }

        var result = await repository.GetVisibleStarsAsync(
            context.LatitudeDeg,
            context.LongitudeDeg,
            timestampUtc,
            magnitudeLimit: effectiveMagnitude,
            minMaxAltitudeDeg: catalogOptions.MinMaxAltitudeDegrees,
            topN: fetchCount,
            stratified: catalogOptions.StratifiedSelection,
            raBins: catalogOptions.RightAscensionBins,
            decBands: catalogOptions.DeclinationBands,
            screenWidth: context.Engine.Width,
            screenHeight: context.Engine.Height,
            engine: context.Engine).ConfigureAwait(false);

        if (result.IsFailure)
        {
            if (result.Error is not OperationCanceledException)
            {
                _logger.LogError(result.Error, "Celestial annotations: failed to resolve automatic star annotations.");
            }
            return Array.Empty<ResolvedAnnotation>();
        }

        var candidates = result.Value;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Celestial annotations: repository returned {CandidateCount} candidate star(s) (effective mag ≤ {MagLimit:F1}, desired {Desired}, fetch {Fetch}).",
                    candidates.Count,
                    effectiveMagnitude,
                    desiredCount,
                    fetchCount);
            }

    static string? ResolveLabel(Star star)
        {
            if (!string.IsNullOrWhiteSpace(star.CommonName))
            {
                return star.CommonName;
            }

            if (!string.IsNullOrWhiteSpace(star.Designation))
            {
                return star.Designation;
            }

            return star.HarvardRevisedNumber is int hr ? $"HR {hr}" : null;
        }

        var labeled = candidates
            .Where(star => star.Magnitude <= effectiveMagnitude)
            .Select(star => new { Star = star, Label = ResolveLabel(star) })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Label))
            .OrderBy(entry => entry.Star.Magnitude)
            .Take(desiredCount)
            .ToList();

        if (labeled.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Celestial annotations: no labeled stars after applying name filter; first candidate magnitudes: {Magnitudes}.",
                    string.Join(
                        ", ",
                        candidates
                            .OrderBy(star => star.Magnitude)
                            .Take(5)
                            .Select(star => star.Magnitude.ToString("F1", CultureInfo.InvariantCulture))));
            }
            return Array.Empty<ResolvedAnnotation>();
        }

        var annotations = new List<ResolvedAnnotation>(labeled.Count);
        foreach (var entry in labeled)
        {
            var star = entry.Star;
            var label = entry.Label!;
            annotations.Add(new ResolvedAnnotation(
                label,
                star,
                DefaultStarColor,
                cache.StarRingRadius,
                UseFaintRing: false,
                cache.StarLabelColor));
        }

        return annotations;
    }

    private async Task<IReadOnlyList<PlanetMark>> ComputePlanetMarks(
        ObservatoryLocationOptions location,
        StarCatalogOptions options,
        DateTime timestampUtc,
        HashSet<PlanetBody> requestedBodies,
        CancellationToken cancellationToken)
    {
        if (requestedBodies.Count == 0) return Array.Empty<PlanetMark>();
        cancellationToken.ThrowIfCancellationRequested();

        var criteria = PlanetVisibilityCriteria.FromOptions(options, requestedBodies);

        foreach (var body in requestedBodies)
            if (!criteria.IsBodyEnabled(body)) LogPlanetSuppressed(body);

        if (!criteria.ShouldCompute) return Array.Empty<PlanetMark>();

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPlanetRepository>();

        var result = await repository.GetVisiblePlanetsAsync(
            latitudeDeg: location.LatitudeDegrees,
            longitudeDeg: location.LongitudeDegrees,
            utc: timestampUtc,
            criteria: criteria,
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            if (result.Error is OperationCanceledException oce) throw oce;
            _logger.LogError(result.Error, "Celestial annotations: planet repository failed to provide marks.");
            return Array.Empty<PlanetMark>();
        }

        var marks = result.Value;
        if (marks.Count == 0) return Array.Empty<PlanetMark>();

        var filtered = new List<PlanetMark>(marks.Count);
        foreach (var mark in marks)
            if (requestedBodies.Contains(mark.Body)) filtered.Add(mark);

        return filtered.Count == 0 ? Array.Empty<PlanetMark>() : filtered;
    }

    private void LogPlanetSuppressed(PlanetBody body)
    {
        lock (_planetWarningLock)
        {
            // only once
            _suppressedPlanetWarnings.Add(body);
        }
    }

    private static bool ShouldAnnotatePlanets(StarCatalogOptions options, AnnotationCache cache)
        => cache.PlanetBodyLookup.Count > 0 && cache.AnnotatePlanets &&
           (options.IncludePlanets || options.IncludeMoon || options.IncludeOuterPlanets || options.IncludeSun);

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
            PathEffect = SKPathEffect.CreateDash(new float[]{4f,4f}, 0)
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

    private static IReadOnlyDictionary<string, Star> BuildCatalogStarIndex(IConstellationCatalog catalog)
    {
        var index = new Dictionary<string, Star>(StringComparer.OrdinalIgnoreCase);
        var constellations = catalog.GetStarLookup();

        foreach (var constellation in constellations.Values)
        {
            foreach (var star in constellation)
            {
                AddIfMissing(star.Designation, star);
                AddIfMissing(star.CommonName, star);

                if (star.HarvardRevisedNumber is int hr)
                {
                    AddIfMissing($"HR {hr}", star);
                    AddIfMissing(hr.ToString(CultureInfo.InvariantCulture), star);
                }
            }
        }

        return index;

        void AddIfMissing(string? key, Star starValue)
        {
            if (!string.IsNullOrWhiteSpace(key) && !index.ContainsKey(key))
            {
                index[key] = starValue;
            }
        }
    }

    private static void ClampRectToCanvas(ref SKRect rect, int width, int height)
    {
        const float margin = 4f;
        var minX = margin;
        var minY = margin;
        var maxX = Math.Max(width - margin, minX);
        var maxY = Math.Max(height - margin, minY);

        var offsetX = 0f;
        if (rect.Left < minX) offsetX = minX - rect.Left;
        else if (rect.Right > maxX) offsetX = maxX - rect.Right;

        var offsetY = 0f;
        if (rect.Top < minY) offsetY = minY - rect.Top;
        else if (rect.Bottom > maxY) offsetY = maxY - rect.Bottom;

        rect.Offset(offsetX, offsetY);
    }

    private sealed record ResolvedAnnotation(string Name, Star Star, SKColor RingColor, float RingRadius, bool UseFaintRing, SKColor LabelColor);

    private sealed record AutoStarSelectionSettings(bool Enabled, int Count, double MagnitudeLimit);

    private sealed class AnnotationCache
    {
        public static AnnotationCache Empty { get; } = new(
            Array.Empty<ResolvedAnnotation>(),
            Array.Empty<ResolvedAnnotation>(),
            new HashSet<PlanetBody>(),
            annotatePlanets: false,
            8.0f,
            DefaultStarLabelColor,
            DefaultPlanetLabelColor,
            DefaultDeepSkyLabelColor,
            DefaultPlanetRingRadius,
            DefaultStarRingRadius,
            DefaultDeepSkyRingRadius,
            new AutoStarSelectionSettings(false, 0, 0));

        public AnnotationCache(
            IReadOnlyList<ResolvedAnnotation> starTargets,
            IReadOnlyList<ResolvedAnnotation> deepSkyTargets,
            HashSet<PlanetBody> planetBodies,
            bool annotatePlanets,
            float labelFontSize,
            SKColor starLabelColor,
            SKColor planetLabelColor,
            SKColor deepSkyLabelColor,
            float planetRingRadius,
            float starRingRadius,
            float deepSkyRingRadius,
            AutoStarSelectionSettings autoStarSelection)
        {
            StarTargets = starTargets;
            DeepSkyTargets = deepSkyTargets;
            PlanetBodyLookup = planetBodies;
            AnnotatePlanets = annotatePlanets;
            LabelFontSize = labelFontSize;
            StarLabelColor = starLabelColor;
            PlanetLabelColor = planetLabelColor;
            DeepSkyLabelColor = deepSkyLabelColor;
            PlanetRingRadius = planetRingRadius;
            StarRingRadius = starRingRadius;
            DeepSkyRingRadius = deepSkyRingRadius;
            AutoStarSelection = autoStarSelection;
        }

        public IReadOnlyList<ResolvedAnnotation> StarTargets { get; }
        public IReadOnlyList<ResolvedAnnotation> DeepSkyTargets { get; }
        public HashSet<PlanetBody> PlanetBodyLookup { get; }
    public bool AnnotatePlanets { get; }
        public float LabelFontSize { get; }
        public SKColor StarLabelColor { get; }
        public SKColor PlanetLabelColor { get; }
        public SKColor DeepSkyLabelColor { get; }
        public float PlanetRingRadius { get; }
        public float StarRingRadius { get; }
        public float DeepSkyRingRadius { get; }
        public AutoStarSelectionSettings AutoStarSelection { get; }

        public bool IsEmpty => StarTargets.Count == 0 && !AutoStarSelection.Enabled && DeepSkyTargets.Count == 0 && PlanetBodyLookup.Count == 0;
    }
}