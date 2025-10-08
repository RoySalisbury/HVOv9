#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Cameras
{
    /// <summary>
    /// Synthetic fisheye camera adapter that renders a realistic all-sky projection using the starfield engine.
    /// </summary>
    public sealed class MockFisheyeCameraAdapter : ICameraAdapter
    {
        private const int RandomFillerStars = 0;

        // Provide these constants so other components (e.g., HYG repo) can rely on them.
        public const ProjectionModel DefaultProjectionModel = ProjectionModel.Equidistant;
        public const double DefaultHorizonPadding = 0.98;
        public const double DefaultFovDeg = 185.0;

    private readonly IOptionsMonitor<ObservatoryLocationOptions> _locationMonitor;
        private readonly IOptionsMonitor<StarCatalogOptions> _catalogOptions;
        private readonly IOptionsMonitor<CardinalDirectionsOptions> _cardinalMonitor;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IRigProvider _rigProvider;
        private readonly ILogger<MockFisheyeCameraAdapter> _logger;
        private readonly Random _random = new();

        private bool _initialized;

        public MockFisheyeCameraAdapter(
            IOptionsMonitor<ObservatoryLocationOptions> locationMonitor,
            IOptionsMonitor<StarCatalogOptions> catalogOptions,
            IOptionsMonitor<CardinalDirectionsOptions> cardinalOptions,
            IServiceScopeFactory scopeFactory,
            IRigProvider rigProvider,
            ILogger<MockFisheyeCameraAdapter>? logger = null)
        {
            _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));
            _catalogOptions = catalogOptions ?? throw new ArgumentNullException(nameof(catalogOptions));
            _cardinalMonitor = cardinalOptions ?? throw new ArgumentNullException(nameof(cardinalOptions));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _rigProvider = rigProvider ?? throw new ArgumentNullException(nameof(rigProvider));
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
            _logger.LogInformation(
                "Mock fisheye camera initialized for latitude {LatitudeDegrees}°, longitude {LongitudeDegrees}°.",
                location.LatitudeDegrees,
                location.LongitudeDegrees);

            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<bool>> ShutdownAsync(CancellationToken cancellationToken)
        {
            _initialized = false;
            _logger.LogInformation("Mock fisheye camera shutdown requested.");
            return Task.FromResult(Result<bool>.Success(true));
        }

        public async Task<Result<CapturedImage>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken)
        {
            if (!_initialized)
            {
                return Result<CapturedImage>.Failure(new InvalidOperationException("Camera adapter has not been initialized."));
            }

            SKBitmap? starfield = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nowUtc = DateTime.UtcNow;
                var location = _locationMonitor.CurrentValue;
                var catalogConfig = _catalogOptions.CurrentValue;
                var rig = _rigProvider.Current;
                var projector = RigFactory.CreateProjector(rig, horizonPadding: DefaultHorizonPadding);
                var frameWidth = projector.WidthPx;
                var frameHeight = projector.HeightPx;
                var flipHorizontal = _cardinalMonitor.CurrentValue.SwapEastWest;

                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IStarRepository>();

                // Build star list (Result<T> aware)
                List<Star> catalogStars;
                if (catalogConfig.IncludeConstellationHighlight)
                {
                    var visibleConstellations = await repository.GetVisibleByConstellationAsync(
                        latitudeDeg: location.LatitudeDegrees,
                        longitudeDeg: location.LongitudeDegrees,
                        utc: nowUtc,
                        magnitudeLimit: catalogConfig.MagnitudeLimit,
                        minMaxAltitudeDeg: catalogConfig.MinMaxAltitudeDegrees,
                        screenWidth: frameWidth,
                        screenHeight: frameHeight);

                    if (visibleConstellations.IsFailure)
                    {
                        return Result<CapturedImage>.Failure(visibleConstellations.Error ?? new InvalidOperationException("Constellation query failed."));
                    }

                    var starsWithLabels = new List<Star>(catalogConfig.TopStarCount);
                    foreach (var group in visibleConstellations.Value)
                    {
                        var count = 0;
                        foreach (var star in group.Stars)
                        {
                            starsWithLabels.Add(star);
                            count++;
                            if (count >= catalogConfig.ConstellationStarCap) break;
                        }
                    }

                    if (starsWithLabels.Count < catalogConfig.TopStarCount)
                    {
                        var fallback = await repository.GetVisibleStarsAsync(
                            latitudeDeg: location.LatitudeDegrees,
                            longitudeDeg: location.LongitudeDegrees,
                            utc: nowUtc,
                            magnitudeLimit: catalogConfig.MagnitudeLimit,
                            minMaxAltitudeDeg: catalogConfig.MinMaxAltitudeDegrees,
                            topN: catalogConfig.TopStarCount,
                            stratified: catalogConfig.StratifiedSelection,
                            raBins: catalogConfig.RightAscensionBins,
                            decBands: catalogConfig.DeclinationBands,
                            screenWidth: frameWidth,
                            screenHeight: frameHeight);

                        if (fallback.IsFailure)
                        {
                            return Result<CapturedImage>.Failure(fallback.Error ?? new InvalidOperationException("Star query failed."));
                        }

                        foreach (var star in fallback.Value)
                        {
                            if (starsWithLabels.Count >= catalogConfig.TopStarCount) break;
                            starsWithLabels.Add(star);
                        }
                    }

                    catalogStars = starsWithLabels;
                }
                else
                {
                    var starsResult = await repository.GetVisibleStarsAsync(
                        latitudeDeg: location.LatitudeDegrees,
                        longitudeDeg: location.LongitudeDegrees,
                        utc: nowUtc,
                        magnitudeLimit: catalogConfig.MagnitudeLimit,
                        minMaxAltitudeDeg: catalogConfig.MinMaxAltitudeDegrees,
                        topN: catalogConfig.TopStarCount,
                        stratified: catalogConfig.StratifiedSelection,
                        raBins: catalogConfig.RightAscensionBins,
                        decBands: catalogConfig.DeclinationBands,
                        screenWidth: frameWidth,
                        screenHeight: frameHeight);

                    if (starsResult.IsFailure)
                    {
                        return Result<CapturedImage>.Failure(starsResult.Error ?? new InvalidOperationException("Star query failed."));
                    }

                    catalogStars = new List<Star>(starsResult.Value);
                }

                // Planets (cheap ephemeris from current code-path)
                IReadOnlyList<PlanetMark> planetMarks = Array.Empty<PlanetMark>();
                if (ShouldComputePlanets(catalogConfig))
                {
                    var computed = PlanetMarks.Compute(
                        latitudeDeg: location.LatitudeDegrees,
                        longitudeDeg: location.LongitudeDegrees,
                        utc: nowUtc,
                        includeUranusNeptune: catalogConfig.IncludeOuterPlanets,
                        includeSun: catalogConfig.IncludeSun);

                    if (computed.Count > 0)
                    {
                        var filtered = new List<PlanetMark>(computed.Count);
                        foreach (var mark in computed)
                        {
                            if (!ShouldIncludePlanet(mark, catalogConfig)) continue;
                            filtered.Add(mark);
                            catalogStars.Add(mark.Star);
                        }
                        planetMarks = filtered;
                    }
                }

                var engine = new StarFieldEngine(
                    projector,
                    latitudeDeg: location.LatitudeDegrees,
                    longitudeDeg: location.LongitudeDegrees,
                    utcUtc: nowUtc,
                    flipHorizontal: flipHorizontal,
                    applyRefraction: true);

                starfield = engine.Render(
                    catalogStars,
                    planets: planetMarks,
                    randomFillerCount: RandomFillerStars,
                    randomSeed: null,
                    dimFaintStars: true,
                    planetOptions: PlanetRenderOptions.Default,
                    out _,
                    out _);

                ApplySensorNoise(starfield, exposure);

                var frameTimestamp = new DateTimeOffset(nowUtc, TimeSpan.Zero);
                var frameContext = new FrameContext(
                    rig,
                    projector,
                    engine,
                    frameTimestamp,
                    location.LatitudeDegrees,
                    location.LongitudeDegrees,
                    flipHorizontal,
                    DefaultHorizonPadding,
                    ApplyRefraction: true);

                var frame = new CapturedImage(
                    starfield,
                    frameTimestamp,
                    exposure,
                    frameContext);
                starfield = null; // ownership transferred to CapturedImage

                _logger.LogTrace(
                    "Mock fisheye camera captured frame at {TimestampUtc} with exposure {ExposureMs} ms, gain {Gain}, and {StarCount} catalog stars.",
                    frame.Timestamp.UtcDateTime,
                    exposure.ExposureMilliseconds,
                    exposure.Gain,
                    catalogStars.Count);

                return Result<CapturedImage>.Success(frame);
            }
            catch (OperationCanceledException ex)
            {
                starfield?.Dispose();
                _logger.LogDebug(ex, "Mock fisheye capture cancelled.");
                return Result<CapturedImage>.Failure(ex);
            }
            catch (Exception ex)
            {
                starfield?.Dispose();
                _logger.LogError(ex, "Mock fisheye capture failed.");
                return Result<CapturedImage>.Failure(ex);
            }
        }

        private void ApplySensorNoise(SKBitmap bitmap, ExposureSettings exposure)
        {
            var noiseLevel = Math.Clamp(exposure.Gain / 480d, 0.012d, 0.09d);
            var twinkleProbability = 0.0015d + exposure.Gain * 0.000012d;

            var span = bitmap.GetPixelSpan();
            for (var i = 0; i < span.Length; i += 4)
            {
                var alpha = span[i + 3];
                var current = span[i + 2];
                var noise = (int)((_random.NextDouble() - 0.5d) * 512 * noiseLevel);
                var twinkleBoost = 0;

                if (current > 225 && _random.NextDouble() < twinkleProbability)
                {
                    twinkleBoost = _random.Next(5, 22);
                }

                var value = (byte)Math.Clamp(current + noise + twinkleBoost, 0, 255);
                span[i] = value;
                span[i + 1] = value;
                span[i + 2] = value;
                span[i + 3] = alpha;
            }
        }

        private static bool ShouldComputePlanets(StarCatalogOptions options)
            => options.IncludePlanets || options.IncludeMoon || options.IncludeOuterPlanets || options.IncludeSun;

        private static bool ShouldIncludePlanet(PlanetMark mark, StarCatalogOptions options) => mark.Body switch
        {
            PlanetBody.Moon => options.IncludeMoon,
            PlanetBody.Sun => options.IncludeSun,
            PlanetBody.Uranus or PlanetBody.Neptune => options.IncludeOuterPlanets,
            _ => options.IncludePlanets
        };
    }
}
