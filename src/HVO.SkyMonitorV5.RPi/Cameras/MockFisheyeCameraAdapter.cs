#nullable enable

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

            var nowUtc = DateTime.UtcNow;
            var location = _locationMonitor.CurrentValue;
            var catalogConfig = _catalogOptions.CurrentValue;

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IStarRepository>();

            // Build star list
            List<Star> catalogStars;
            if (catalogConfig.IncludeConstellationHighlight)
            {
                var groups = await repository.GetVisibleByConstellationAsync(
                    latitudeDeg: location.LatitudeDegrees,
                    longitudeDeg: location.LongitudeDegrees,
                    utc: nowUtc,
                    magnitudeLimit: catalogConfig.MagnitudeLimit,
                    minMaxAltitudeDeg: catalogConfig.MinMaxAltitudeDegrees,
                    screenWidth: FrameWidth,
                    screenHeight: FrameHeight);

                var starsWithLabels = new List<Star>(catalogConfig.TopStarCount);
                foreach (var group in groups)
                {
                    var count = 0;
                    foreach (var s in group.Stars)
                    {
                        starsWithLabels.Add(s);
                        if (++count >= catalogConfig.ConstellationStarCap) break;
                    }
                }

                if (starsWithLabels.Count < catalogConfig.TopStarCount)
                {
                    var fallback = await repository.GetVisibleStarsAsync(
                        location.LatitudeDegrees, location.LongitudeDegrees, nowUtc,
                        catalogConfig.MagnitudeLimit, catalogConfig.MinMaxAltitudeDegrees,
                        catalogConfig.TopStarCount, catalogConfig.StratifiedSelection,
                        catalogConfig.RightAscensionBins, catalogConfig.DeclinationBands,
                        FrameWidth, FrameHeight);

                    foreach (var s in fallback)
                    {
                        if (starsWithLabels.Count >= catalogConfig.TopStarCount) break;
                        starsWithLabels.Add(s);
                    }
                }

                catalogStars = starsWithLabels;
            }
            else
            {
                catalogStars = await repository.GetVisibleStarsAsync(
                    location.LatitudeDegrees, location.LongitudeDegrees, nowUtc,
                    catalogConfig.MagnitudeLimit, catalogConfig.MinMaxAltitudeDegrees,
                    catalogConfig.TopStarCount, catalogConfig.StratifiedSelection,
                    catalogConfig.RightAscensionBins, catalogConfig.DeclinationBands,
                    FrameWidth, FrameHeight);
            }

            // Planets
            IReadOnlyList<PlanetMark> planetMarks = Array.Empty<PlanetMark>();
            if (ShouldComputePlanets(catalogConfig))
            {
                var computed = PlanetMarks.Compute(
                    latitudeDeg: location.LatitudeDegrees,
                    longitudeDeg: location.LongitudeDegrees,
                    utc: nowUtc,
                    includeUranusNeptune: catalogConfig.IncludeOuterPlanets,
                    includeSun: catalogConfig.IncludeSun);

                var filtered = new List<PlanetMark>(computed.Count);
                foreach (var p in computed)
                {
                    if (!ShouldIncludePlanet(p, catalogConfig)) continue;
                    filtered.Add(p);
                    catalogStars.Add(p.Star); // draw a point underneath glyph
                }
                if (filtered.Count > 0) planetMarks = filtered;
            }

            _logger.LogInformation("Planets to render: {List}", string.Join(", ", planetMarks.Select(p => p.Name)));

            // Render (equidistant fisheye; apparent sky)
            var engine = new StarFieldEngine(
                FrameWidth, FrameHeight,
                location.LatitudeDegrees, location.LongitudeDegrees, nowUtc,
                projection: DefaultProjection,
                horizonPaddingPct: DefaultHorizonPadding,
                flipHorizontal: _cardinalMonitor.CurrentValue.SwapEastWest,
                fovDeg: DefaultFovDeg,
                applyRefraction: true);

            using var sky = engine.Render(
                catalogStars, planetMarks,
                RandomFillerStars, randomSeed: null,
                dimFaintStars: true,
                PlanetRenderOptions.Default,
                out _, out _);

            ApplySensorNoise(sky, exposure);

            var pixelBuffer = FramePixelBuffer.FromBitmap(sky);
            using var image = SKImage.FromBitmap(sky);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            var bytes = data.ToArray();

            var frame = new CameraFrame(DateTimeOffset.UtcNow, exposure, bytes, "image/png", pixelBuffer);
            _logger.LogTrace("Mock fisheye captured {Stars} stars at {Utc}.", catalogStars.Count, frame.Timestamp.UtcDateTime);
            return Result<CameraFrame>.Success(frame);
        }
        catch (OperationCanceledException oce)
        {
            _logger.LogDebug(oce, "Capture cancelled.");
            return Result<CameraFrame>.Failure(oce);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Capture failed.");
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
