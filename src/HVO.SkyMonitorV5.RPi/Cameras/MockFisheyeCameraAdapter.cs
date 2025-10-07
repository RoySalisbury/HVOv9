#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Cameras;

public sealed class MockFisheyeCameraAdapter : ICameraAdapter
{
    private const int FrameWidth = 1280;
    private const int FrameHeight = 960;
    private const int RandomFillerStars = 0;

    internal const FisheyeModel DefaultProjection = FisheyeModel.Equidistant;
    internal const double DefaultHorizonPadding = 0.98;
    internal const double DefaultFovDeg = 185.0; // match Stellarium view

    private readonly IOptionsMonitor<ObservatoryLocationOptions> _locationMonitor;
    private readonly IOptionsMonitor<StarCatalogOptions> _catalogOptions;
    private readonly IOptionsMonitor<CardinalDirectionsOptions> _cardinalMonitor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MockFisheyeCameraAdapter> _logger;
    private readonly Random _random = new();

    private bool _initialized;

    public MockFisheyeCameraAdapter(
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor,
        IOptionsMonitor<StarCatalogOptions> catalogOptions,
        IOptionsMonitor<CardinalDirectionsOptions> cardinalOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<MockFisheyeCameraAdapter>? logger = null)
    {
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));
        _catalogOptions = catalogOptions ?? throw new ArgumentNullException(nameof(catalogOptions));
        _cardinalMonitor = cardinalOptions ?? throw new ArgumentNullException(nameof(cardinalOptions));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? NullLogger<MockFisheyeCameraAdapter>.Instance;
    }

    public CameraDescriptor Descriptor { get; } = new(
        Manufacturer: "HVO",
        Model: "Mock Fisheye AllSky",
        DriverVersion: "2.0.0",
        AdapterName: nameof(MockFisheyeCameraAdapter),
        Capabilities: new[] { "Synthetic", "StackingCompatible", "FisheyeProjection" });

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<Result<bool>> InitializeAsync(CancellationToken cancellationToken)
    {
        _initialized = true;
        var location = _locationMonitor.CurrentValue;
        _logger.LogInformation("Mock fisheye camera initialized at {Lat}°, {Lon}°.", location.LatitudeDegrees, location.LongitudeDegrees);
        return Task.FromResult(Result<bool>.Success(true));
    }

    public Task<Result<bool>> ShutdownAsync(CancellationToken cancellationToken)
    {
        _initialized = false;
        _logger.LogInformation("Mock fisheye camera shutdown requested.");
        return Task.FromResult(Result<bool>.Success(true));
    }

public async Task<Result<CameraFrame>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken)
{
    if (!_initialized)
        return Result<CameraFrame>.Failure(new InvalidOperationException("Camera adapter has not been initialized."));

    try
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc   = DateTime.UtcNow;
        var location = _locationMonitor.CurrentValue;
        var cfg      = _catalogOptions.CurrentValue;

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IStarRepository>();

        // ----- target counts -----
        var targetTotal = Math.Max(300, cfg.TopStarCount);
        var brightN = Math.Min(12,  (int)Math.Round(targetTotal * 0.04)); // very bright
        var midN    = Math.Min(60,  (int)Math.Round(targetTotal * 0.20)); // mid
        var faintN  = Math.Max(0, targetTotal - brightN - midN);

        // magnitude caps
        var brightCapMag = Math.Min(2.0, cfg.MagnitudeLimit);
        var midCapMag    = Math.Min(4.0, cfg.MagnitudeLimit);
        var faintCapMag  = Math.Min(6.5, cfg.MagnitudeLimit);

        // ----- fetch candidates from repository -----
        var brightResult = await repository.GetVisibleStarsAsync(
            latitudeDeg: location.LatitudeDegrees, longitudeDeg: location.LongitudeDegrees, utc: nowUtc,
            magnitudeLimit: brightCapMag, minMaxAltitudeDeg: cfg.MinMaxAltitudeDegrees,
            topN: brightN, stratified: false, raBins: 0, decBands: 0,
            screenWidth: FrameWidth, screenHeight: FrameHeight).ConfigureAwait(false);

        if (brightResult.IsFailure)
        {
            var error = brightResult.Error ?? new InvalidOperationException("Failed to retrieve bright star set.");
            _logger.LogError(error, "Star repository failed to provide bright visible stars.");
            return Result<CameraFrame>.Failure(error);
        }

        int midRaBins   = cfg.RightAscensionBins > 0 ? cfg.RightAscensionBins : 24;
        int midDecBands = cfg.DeclinationBands   > 0 ? cfg.DeclinationBands   : 12;

        var midResult = await repository.GetVisibleStarsAsync(
            latitudeDeg: location.LatitudeDegrees, longitudeDeg: location.LongitudeDegrees, utc: nowUtc,
            magnitudeLimit: midCapMag, minMaxAltitudeDeg: cfg.MinMaxAltitudeDegrees,
            topN: midN, stratified: true, raBins: midRaBins, decBands: midDecBands,
            screenWidth: FrameWidth, screenHeight: FrameHeight).ConfigureAwait(false);

        if (midResult.IsFailure)
        {
            var error = midResult.Error ?? new InvalidOperationException("Failed to retrieve mid-range star set.");
            _logger.LogError(error, "Star repository failed to provide mid-range visible stars.");
            return Result<CameraFrame>.Failure(error);
        }

        var faintResult = await repository.GetVisibleStarsAsync(
            latitudeDeg: location.LatitudeDegrees, longitudeDeg: location.LongitudeDegrees, utc: nowUtc,
            magnitudeLimit: faintCapMag, minMaxAltitudeDeg: cfg.MinMaxAltitudeDegrees,
            topN: Math.Max(faintN * 2, faintN + 200), // over-request; we’ll thin in screen space later
            stratified: true, raBins: Math.Max(30, midRaBins + 6), decBands: Math.Max(15, midDecBands + 3),
            screenWidth: FrameWidth, screenHeight: FrameHeight).ConfigureAwait(false);

        if (faintResult.IsFailure)
        {
            var error = faintResult.Error ?? new InvalidOperationException("Failed to retrieve faint star set.");
            _logger.LogError(error, "Star repository failed to provide faint visible stars.");
            return Result<CameraFrame>.Failure(error);
        }

        var bright = brightResult.Value;
        var mid = midResult.Value;
        var faintRaw = faintResult.Value;

        // optional constellation sprinkles
        var highlights = new List<Star>();
        if (cfg.IncludeConstellationHighlight)
        {
            var groupsResult = await repository.GetVisibleByConstellationAsync(
                latitudeDeg: location.LatitudeDegrees, longitudeDeg: location.LongitudeDegrees, utc: nowUtc,
                magnitudeLimit: cfg.MagnitudeLimit, minMaxAltitudeDeg: cfg.MinMaxAltitudeDegrees,
                screenWidth: FrameWidth, screenHeight: FrameHeight).ConfigureAwait(false);

            if (groupsResult.IsFailure)
            {
                var error = groupsResult.Error ?? new InvalidOperationException("Failed to retrieve constellation highlights.");
                _logger.LogError(error, "Star repository failed to provide visible constellation highlights.");
                return Result<CameraFrame>.Failure(error);
            }

            int perConst = Math.Max(2, cfg.ConstellationStarCap);
            foreach (var group in groupsResult.Value)
            {
                var taken = 0;
                foreach (var star in group.Stars)
                {
                    highlights.Add(star);
                    if (++taken >= perConst)
                    {
                        break;
                    }
                }
            }
        }

        var candidates = new List<Star>(bright.Count + mid.Count + faintRaw.Count + highlights.Count);
        candidates.AddRange(bright);
        candidates.AddRange(mid);
        candidates.AddRange(highlights);
        candidates.AddRange(faintRaw);
        candidates.Sort((a, b) => a.Magnitude.CompareTo(b.Magnitude)); // brightest first

        cancellationToken.ThrowIfCancellationRequested();

        // ----- selection engine (same projection/refraction/curve as render) -----
        var selectCurve = new StarSizeCurve(
            RMinPx: 0.8, RMaxPx: 2.9, MMid: 5.6, Slope: 1.40, BrightBoostPerMag: 0.18);

        var engineForSelect = new StarFieldEngine(
            width: FrameWidth, height: FrameHeight,
            latitudeDeg: location.LatitudeDegrees, longitudeDeg: location.LongitudeDegrees,
            utcUtc: nowUtc,
            projection: DefaultProjection,
            horizonPaddingPct: DefaultHorizonPadding,
            flipHorizontal: _cardinalMonitor.CurrentValue.SwapEastWest,
            fovDeg: 184.0, applyRefraction: true,
            sizeCurve: selectCurve);

        // ring quotas (inner→outer), bias toward horizon
        var ringFractions = new[] { 0.06, 0.10, 0.18, 0.28, 0.38 };
        var ringTargets = new int[ringFractions.Length];
        int assigned = 0;
        for (int i = 0; i < ringFractions.Length; i++)
        {
            ringTargets[i] = (i == ringFractions.Length - 1)
                ? targetTotal - assigned
                : (int)Math.Round(targetTotal * ringFractions[i]);
            assigned += ringTargets[i];
        }

        float Cx() => FrameWidth * 0.5f;
        float Cy() => FrameHeight * 0.5f;
        float MaxR() => (float)(Math.Min(Cx(), Cy()) * DefaultHorizonPadding);

        static double MinSepPx(double mag) => mag <= 2.0 ? 18.0 : (mag <= 4.0 ? 10.0 : 5.0);

        int RingIndex(float x, float y)
        {
            var dx = x - Cx(); var dy = y - Cy();
            var r = Math.Sqrt(dx * dx + dy * dy) / MaxR(); // 0..1
            return Math.Clamp((int)Math.Min(4, Math.Floor(r * 5.0)), 0, 4);
        }

        var accepted = new List<(Star star, float x, float y, double mag, int ring)>(targetTotal + 16);

        bool FarEnough(float x, float y, double mag)
        {
            var minSep = MinSepPx(mag);
            foreach (var a in accepted)
            {
                var dx = x - a.x; var dy = y - a.y;
                if ((dx * dx + dy * dy) < minSep * minSep) return false;
            }
            return true;
        }

        foreach (var s in candidates)
        {
            if (!engineForSelect.ProjectStar(s, out var x, out var y)) continue;
            var ring = RingIndex(x, y);
            if (ringTargets[ring] <= 0) continue;
            if (!FarEnough(x, y, s.Magnitude)) continue;

            accepted.Add((s, x, y, s.Magnitude, ring));
            ringTargets[ring]--;
            if (accepted.Count >= targetTotal) break;
        }

        if (accepted.Count < targetTotal)
        {
            foreach (var s in candidates)
            {
                if (!engineForSelect.ProjectStar(s, out var x, out var y)) continue;
                if (!FarEnough(x, y, s.Magnitude)) continue;
                accepted.Add((s, x, y, s.Magnitude, RingIndex(x, y)));
                if (accepted.Count >= targetTotal) break;
            }
        }

        var catalogStars = new List<Star>(accepted.Count);
        foreach (var a in accepted) catalogStars.Add(a.star);

        cancellationToken.ThrowIfCancellationRequested();

        // ----- planets -----
        IReadOnlyList<PlanetMark> planetMarks = Array.Empty<PlanetMark>();
        if (ShouldComputePlanets(cfg))
        {
            var computed = PlanetMarks.Compute(
                latitudeDeg: location.LatitudeDegrees,
                longitudeDeg: location.LongitudeDegrees,
                utc: nowUtc,
                includeUranusNeptune: cfg.IncludeOuterPlanets,
                includeSun: cfg.IncludeSun);

            var filtered = new List<PlanetMark>(computed.Count);
            foreach (var m in computed)
            {
                if (!ShouldIncludePlanet(m, cfg)) continue;
                filtered.Add(m);
                catalogStars.Add(m.Star); // also as points
            }
            if (filtered.Count > 0) planetMarks = filtered;
        }

        // ----- render -----
        var renderCurve = new StarSizeCurve(
            RMinPx: 0.8, RMaxPx: 2.9, MMid: 5.6, Slope: 1.40, BrightBoostPerMag: 0.18);

        var engine = new StarFieldEngine(
            width: FrameWidth, height: FrameHeight,
            latitudeDeg: location.LatitudeDegrees, longitudeDeg: location.LongitudeDegrees,
            utcUtc: nowUtc,
            projection: DefaultProjection,
            horizonPaddingPct: DefaultHorizonPadding,
            flipHorizontal: _cardinalMonitor.CurrentValue.SwapEastWest,
            fovDeg: 184.0, applyRefraction: true,
            sizeCurve: renderCurve);

        using var starfield = engine.Render(
            stars: catalogStars,
            planets: planetMarks,
            randomFillerCount: 0,
            randomSeed: null,
            dimFaintStars: true,
            planetOptions: PlanetRenderOptions.Default,
            out _,
            out _);

        // luma-only noise so star/planet colors stay intact
        ApplySensorNoise(starfield, exposure);

        var pixelBuffer = FramePixelBuffer.FromBitmap(starfield);
        using var image = SKImage.FromBitmap(starfield);
        using var data  = image.Encode(SKEncodedImageFormat.Png, 90);

        var frame = new CameraFrame(DateTimeOffset.UtcNow, exposure, data.ToArray(), "image/png", pixelBuffer);

        _logger.LogTrace("Mock fisheye capture {t} gain={g} stars={stars} planets={planets}",
            frame.Timestamp.UtcDateTime, exposure.Gain, catalogStars.Count, planetMarks.Count);

        return Result<CameraFrame>.Success(frame);
    }
    catch (OperationCanceledException ex)
    {
        _logger.LogDebug(ex, "Mock fisheye capture cancelled.");
        return Result<CameraFrame>.Failure(ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Mock fisheye capture failed.");
        return Result<CameraFrame>.Failure(ex);
    }
}

private void ApplySensorNoise(SKBitmap bitmap, ExposureSettings exposure)
{
    // Tunables
    double noiseLevel = Math.Clamp(exposure.Gain / 480d, 0.012d, 0.09d);
    double twinkleProbability = 0.0015d + exposure.Gain * 0.000012d;
    const double kMin = 0.6;   // clamp luminance scale to avoid crushing colors
    const double kMax = 1.4;

    var span = bitmap.GetPixelSpan();            // BGRA for SkiaSharp default
    for (int i = 0; i < span.Length; i += 4)
    {
        byte b = span[i + 0];
        byte g = span[i + 1];
        byte r = span[i + 2];
        byte a = span[i + 3];
        if (a == 0) continue;                    // skip transparent

        // Rec.709 luminance (don’t change hue/saturation)
        double Y = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        int noise = (int)((_random.NextDouble() - 0.5d) * 512 * noiseLevel);

        // Tiny chance to “sparkle” very bright points (stars)
        int twinkle = (Y > 225 && _random.NextDouble() < twinkleProbability)
            ? _random.Next(5, 22)
            : 0;

        double Y2 = Math.Clamp(Y + noise + twinkle, 0, 255);

        // Scale RGB uniformly by luminance ratio → hue/saturation preserved
        double k = (Y <= 1.0) ? (Y2 == 0 ? 0.0 : Y2) : (Y2 / Y);
        k = Math.Clamp(k, kMin, kMax);

        span[i + 0] = (byte)Math.Clamp(Math.Round(b * k), 0, 255);
        span[i + 1] = (byte)Math.Clamp(Math.Round(g * k), 0, 255);
        span[i + 2] = (byte)Math.Clamp(Math.Round(r * k), 0, 255);
        span[i + 3] = a;
    }
}

    private static bool ShouldComputePlanets(StarCatalogOptions o)
        => o.IncludePlanets || o.IncludeMoon || o.IncludeOuterPlanets || o.IncludeSun;

    private static bool ShouldIncludePlanet(PlanetMark p, StarCatalogOptions o) => p.Body switch
    {
        PlanetBody.Moon => o.IncludeMoon,
        PlanetBody.Sun => o.IncludeSun,
        PlanetBody.Uranus or PlanetBody.Neptune => o.IncludeOuterPlanets,
        _ => o.IncludePlanets
    };
}
