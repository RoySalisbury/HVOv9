#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Cameras;

/// <summary>
/// Synthetic fisheye camera adapter that renders a realistic all-sky projection using the starfield engine.
/// </summary>
public sealed class MockCameraAdapter : CameraAdapterBase
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
    private readonly Random _random = new();

    public MockCameraAdapter(
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor,
        IOptionsMonitor<StarCatalogOptions> catalogOptions,
        IOptionsMonitor<CardinalDirectionsOptions> cardinalOptions,
        IServiceScopeFactory scopeFactory,
        RigSpec rigSpec,
        ILogger<MockCameraAdapter>? logger = null)
        : base(
            EnsureRigDescriptor(rigSpec),
            logger ?? NullLogger<MockCameraAdapter>.Instance)
    {
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));
        _catalogOptions = catalogOptions ?? throw new ArgumentNullException(nameof(catalogOptions));
        _cardinalMonitor = cardinalOptions ?? throw new ArgumentNullException(nameof(cardinalOptions));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    protected override Task<Result<bool>> OnInitializeAsync(CancellationToken cancellationToken)
    {
        var location = _locationMonitor.CurrentValue;
        Logger.LogInformation(
            "Mock camera initialized for latitude {LatitudeDegrees}°, longitude {LongitudeDegrees}°.",
            location.LatitudeDegrees,
            location.LongitudeDegrees);

        return Task.FromResult(Result<bool>.Success(true));
    }

    protected override Task<Result<bool>> OnShutdownAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Mock camera shutdown requested.");
        return Task.FromResult(Result<bool>.Success(true));
    }

    protected override async Task<Result<AdapterFrame>> CaptureFrameAsync(ExposureSettings exposure, CancellationToken cancellationToken)
    {
        SKBitmap? starfield = null;
        StarFieldEngine? engine = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nowUtc = DateTime.UtcNow;
            var location = _locationMonitor.CurrentValue;
            var catalogConfig = _catalogOptions.CurrentValue;
            var flipHorizontal = _cardinalMonitor.CurrentValue.SwapEastWest;

            engine = new StarFieldEngine(
                Rig,
                latitudeDeg: location.LatitudeDegrees,
                longitudeDeg: location.LongitudeDegrees,
                utcUtc: nowUtc,
                flipHorizontal: flipHorizontal,
                applyRefraction: true,
                horizonPadding: DefaultHorizonPadding);

            var frameWidth = engine.Width;
            var frameHeight = engine.Height;

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IStarRepository>();

            // Build star list (Result<T> aware)
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
                screenHeight: frameHeight,
                engine: engine);

            if (starsResult.IsFailure)
            {
                return Result<AdapterFrame>.Failure(starsResult.Error ?? new InvalidOperationException("Star query failed."));
            }

            var catalogStars = new List<Star>(starsResult.Value);

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
            var adapterFrame = new AdapterFrame(
                starfield,
                engine,
                frameTimestamp,
                location.LatitudeDegrees,
                location.LongitudeDegrees,
                flipHorizontal,
                DefaultHorizonPadding,
                ApplyRefraction: true,
                Exposure: exposure,
                StarCount: catalogStars.Count,
                PlanetCount: planetMarks.Count);

            // Transfer ownership to the adapter frame
            starfield = null;
            engine = null;

            return Result<AdapterFrame>.Success(adapterFrame);
        }
        catch (OperationCanceledException ex)
        {
            starfield?.Dispose();
            engine?.Dispose();
            Logger.LogDebug(ex, "Mock camera capture cancelled.");
            return Result<AdapterFrame>.Failure(ex);
        }
        catch (Exception)
        {
            starfield?.Dispose();
            throw;
        }
        finally
        {
            engine?.Dispose();
            starfield?.Dispose();
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

    private static RigSpec EnsureRigDescriptor(RigSpec? rig)
    {
        if (rig is null)
        {
            throw new ArgumentNullException(nameof(rig));
        }

        return rig.Descriptor is not null
            ? rig
            : rig with { Descriptor = CreateDefaultDescriptor() };
    }

    private static CameraDescriptor CreateDefaultDescriptor() => new(
        Manufacturer: "HVO",
        Model: "Mock Fisheye AllSky",
        DriverVersion: "2.0.0",
        AdapterName: nameof(MockCameraAdapter),
        Capabilities: new[] { "Synthetic", "StackingCompatible", "FisheyeProjection" });
}
