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

/// <summary>
/// Synthetic fisheye camera adapter that renders a realistic all-sky projection using the starfield engine.
/// </summary>
public sealed class MockFisheyeCameraAdapter : ICameraAdapter
{
    private const int FrameWidth = 1280;
    private const int FrameHeight = 960;
    private const int RandomFillerStars = 0;

    private readonly IOptionsMonitor<ObservatoryLocationOptions> _locationMonitor;
    private readonly IOptionsMonitor<StarCatalogOptions> _catalogOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MockFisheyeCameraAdapter> _logger;
    private readonly Random _random = new();

    private bool _initialized;

    public MockFisheyeCameraAdapter(
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor,
        IOptionsMonitor<StarCatalogOptions> catalogOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<MockFisheyeCameraAdapter>? logger = null)
    {
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));
        _catalogOptions = catalogOptions ?? throw new ArgumentNullException(nameof(catalogOptions));
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

    public async Task<Result<CameraFrame>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            return Result<CameraFrame>.Failure(new InvalidOperationException("Camera adapter has not been initialized."));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nowUtc = DateTime.UtcNow;
            var location = _locationMonitor.CurrentValue;
            var catalogConfig = _catalogOptions.CurrentValue;

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IStarRepository>();

            List<Star> catalogStars;

            if (catalogConfig.IncludeConstellationHighlight)
            {
                var visibleConstellations = await repository.GetVisibleByConstellationAsync(
                    latitudeDeg: location.LatitudeDegrees,
                    longitudeDeg: location.LongitudeDegrees,
                    utc: nowUtc,
                    magnitudeLimit: catalogConfig.MagnitudeLimit,
                    minMaxAltitudeDeg: catalogConfig.MinMaxAltitudeDegrees,
                    screenWidth: FrameWidth,
                    screenHeight: FrameHeight);

                var starsWithLabels = new List<Star>(catalogConfig.TopStarCount);

                foreach (var group in visibleConstellations)
                {
                    var count = 0;
                    foreach (var star in group.Stars)
                    {
                        starsWithLabels.Add(star);
                        count++;
                        if (count >= catalogConfig.ConstellationStarCap)
                        {
                            break;
                        }
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
                        screenWidth: FrameWidth,
                        screenHeight: FrameHeight);

                    foreach (var star in fallback)
                    {
                        if (starsWithLabels.Count >= catalogConfig.TopStarCount)
                        {
                            break;
                        }

                        starsWithLabels.Add(star);
                    }
                }

                catalogStars = starsWithLabels;
            }
            else
            {
                catalogStars = await repository.GetVisibleStarsAsync(
                    latitudeDeg: location.LatitudeDegrees,
                    longitudeDeg: location.LongitudeDegrees,
                    utc: nowUtc,
                    magnitudeLimit: catalogConfig.MagnitudeLimit,
                    minMaxAltitudeDeg: catalogConfig.MinMaxAltitudeDegrees,
                    topN: catalogConfig.TopStarCount,
                    stratified: catalogConfig.StratifiedSelection,
                    raBins: catalogConfig.RightAscensionBins,
                    decBands: catalogConfig.DeclinationBands,
                    screenWidth: FrameWidth,
                    screenHeight: FrameHeight);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var engine = new StarFieldEngine(
                FrameWidth,
                FrameHeight,
                location.LatitudeDegrees,
                location.LongitudeDegrees,
                nowUtc,
                projection: FisheyeModel.EquisolidAngle,
                horizonPaddingPct: 0.98);

            using var starfield = engine.Render(
                catalogStars,
                RandomFillerStars,
                randomSeed: null,
                dimFaintStars: true,
                out _);

            ApplySensorNoise(starfield, exposure);

            var pixelBuffer = FramePixelBuffer.FromBitmap(starfield);

            using var image = SKImage.FromBitmap(starfield);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            var bytes = data.ToArray();

            var frame = new CameraFrame(DateTimeOffset.UtcNow, exposure, bytes, "image/png", pixelBuffer);

            _logger.LogTrace(
                "Mock fisheye camera captured frame at {TimestampUtc} with exposure {ExposureMs} ms, gain {Gain}, and {StarCount} catalog stars.",
                frame.Timestamp.UtcDateTime,
                exposure.ExposureMilliseconds,
                exposure.Gain,
                catalogStars.Count);

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
}
