#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.Infrastructure;
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
public class MockCameraAdapter : CameraAdapterBase
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
    private readonly IObservatoryClock _observatoryClock;
    private readonly Random _random = new();

    private const double FallbackExposureMilliseconds = 35.0;
    private const double ReadoutOverheadBaseMilliseconds = 18.0;
    private const double ReadoutOverheadGainScaleMilliseconds = 12.0;
    private const double ExposureJitterFraction = 0.018; // keep small so delays stay stable frame-to-frame
    private const double SensorNoiseScale = 0.25; // reduce synthetic noise amplitude for less aggressive grain

    private const string DisableSensorNoiseVariable = "HVO_DISABLE_SENSOR_NOISE";
    private static bool _sensorNoiseDisabledLogged;
    private static readonly object SensorNoiseLogLock = new();

    protected readonly record struct SensorResponseProfile(
        double BrightnessScale,
        double LuminanceNoise,
        double ChrominanceNoise,
        double TwinkleProbability,
        byte TwinkleThreshold,
        int TwinkleBoostMin,
        int TwinkleBoostMax);

    protected Random Random => _random;

    public MockCameraAdapter(
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor,
        IOptionsMonitor<StarCatalogOptions> catalogOptions,
        IOptionsMonitor<CardinalDirectionsOptions> cardinalOptions,
        IServiceScopeFactory scopeFactory,
        RigSpec rigSpec,
        IObservatoryClock observatoryClock,
        ILogger<MockCameraAdapter>? logger = null)
        : base(
            EnsureRigDescriptor(rigSpec),
            observatoryClock,
            logger ?? NullLogger<MockCameraAdapter>.Instance)
    {
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));
        _catalogOptions = catalogOptions ?? throw new ArgumentNullException(nameof(catalogOptions));
        _cardinalMonitor = cardinalOptions ?? throw new ArgumentNullException(nameof(cardinalOptions));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _observatoryClock = observatoryClock ?? throw new ArgumentNullException(nameof(observatoryClock));
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

    protected override async Task<Result<AdapterFrame>> AcquireImageAsync(ExposureSettings exposure, CancellationToken cancellationToken)
    {
        SKBitmap? starfield = null;
        StarFieldEngine? engine = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var usingFallbackExposure = exposure.ExposureMilliseconds <= 0;
            var simulatedDuration = ComputeSimulatedExposureDuration(exposure);

            if (usingFallbackExposure)
            {
                Logger.LogTrace(
                    "Mock camera received zero-length exposure; applying fallback duration of {FallbackMs} ms for realism.",
                    FallbackExposureMilliseconds);
            }

            if (simulatedDuration > TimeSpan.Zero)
            {
                Logger.LogTrace(
                    "Simulating mock exposure for {DurationMs:0.##} ms (requested {RequestedMs} ms, gain {Gain}).",
                    simulatedDuration.TotalMilliseconds,
                    exposure.ExposureMilliseconds,
                    exposure.Gain);

                await Task.Delay(simulatedDuration, cancellationToken).ConfigureAwait(false);
            }

            var captureInstant = _observatoryClock.UtcNow;
            var captureUtc = captureInstant.UtcDateTime;
            var location = _locationMonitor.CurrentValue;
            var catalogConfig = _catalogOptions.CurrentValue;
            var flipHorizontal = _cardinalMonitor.CurrentValue.SwapEastWest;

            engine = new StarFieldEngine(
                Rig,
                latitudeDeg: location.LatitudeDegrees,
                longitudeDeg: location.LongitudeDegrees,
                utcUtc: captureUtc,
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
                utc: captureUtc,
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
                    utc: captureUtc,
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

            var frameTimestamp = captureInstant;
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

    protected override Task<Result<AdapterFrame>> PostprocessFrameAsync(AdapterFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsSensorNoiseDisabled())
        {
            LogSensorNoiseDisabledOnce();
            return Task.FromResult(Result<AdapterFrame>.Success(frame));
        }

        ApplySensorNoise(frame.Bitmap, frame.Exposure);
        return Task.FromResult(Result<AdapterFrame>.Success(frame));
    }

    private static bool IsSensorNoiseDisabled()
    {
        var value = Environment.GetEnvironmentVariable(DisableSensorNoiseVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (bool.TryParse(value, out var boolean))
        {
            return boolean;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric != 0;
        }

        return string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private void LogSensorNoiseDisabledOnce()
    {
        if (Volatile.Read(ref _sensorNoiseDisabledLogged))
        {
            return;
        }

        lock (SensorNoiseLogLock)
        {
            if (_sensorNoiseDisabledLogged)
            {
                return;
            }

            Logger.LogDebug(
                "Mock camera sensor noise injection skipped because environment variable {Variable} is enabled.",
                DisableSensorNoiseVariable);
            _sensorNoiseDisabledLogged = true;
        }
    }

    protected virtual void ApplySensorNoise(SKBitmap bitmap, ExposureSettings exposure)
    {
        var profile = BuildSensorResponseProfile(exposure);

        var span = bitmap.GetPixelSpan();
        for (var i = 0; i < span.Length; i += 4)
        {
            var alpha = span[i + 3];
            var baseline = span[i + 2];

            var scaled = Math.Clamp(baseline * profile.BrightnessScale, 0d, 255d);
            var noise = (Random.NextDouble() - 0.5d) * 512d * profile.LuminanceNoise;

            var twinkleBoost = 0d;
            if (scaled >= profile.TwinkleThreshold && Random.NextDouble() < profile.TwinkleProbability)
            {
                twinkleBoost = Random.Next(profile.TwinkleBoostMin, profile.TwinkleBoostMax + 1);
            }

            var value = (byte)Math.Clamp(scaled + noise + twinkleBoost, 0d, 255d);
            span[i] = value;
            span[i + 1] = value;
            span[i + 2] = value;
            span[i + 3] = alpha;
        }

        Logger.LogTrace(
            "Applied synthetic mono sensor response (exposure {ExposureMs} ms, gain {Gain}, brightness x{Brightness:0.00}, noise {Noise:0.000}).",
            exposure.ExposureMilliseconds,
            exposure.Gain,
            profile.BrightnessScale,
            profile.LuminanceNoise);
    }

    protected SensorResponseProfile BuildSensorResponseProfile(ExposureSettings exposure)
    {
        var exposureMs = exposure.ExposureMilliseconds > 0
            ? (double)exposure.ExposureMilliseconds
            : FallbackExposureMilliseconds;

        var gainRatio = NormalizeGain(exposure.Gain);

        var exposureFactor = Math.Pow(exposureMs / 1000d, 0.65d);
        var brightnessScale = 0.45d + exposureFactor;
        brightnessScale *= 1.0d + gainRatio * 0.75d;

        if (Rig.Capabilities.ColorMode == CameraColorMode.Color)
        {
            brightnessScale *= 0.94d;
        }

        var baseNoiseFloor = 0.0075d;
        var gainNoise = gainRatio * 0.065d;
        var exposureNoise = Math.Pow(Math.Min(exposureMs, 12000d) / 8000d, 0.7d) * 0.35d;

        var luminanceNoise = Math.Clamp(baseNoiseFloor + gainNoise + exposureNoise, 0.006d, 0.22d);

        if (Rig.Capabilities.IsCooled)
        {
            luminanceNoise *= 0.65d;
        }

        luminanceNoise *= Rig.Capabilities.ColorMode switch
        {
            CameraColorMode.Monochrome => 0.82d,
            CameraColorMode.Color => 1.05d,
            _ => 1.0d
        };

        luminanceNoise *= SensorNoiseScale;

        var chromaNoise = Rig.Capabilities.ColorMode == CameraColorMode.Color
            ? luminanceNoise * 0.42d
            : luminanceNoise * 0.18d;

        var twinkleProbability = Math.Clamp(
            0.0015d
            + gainRatio * 0.0038d
            + Math.Pow(Math.Min(exposureMs, 15000d) / 6000d, 0.6d) * 0.0075d,
            0.0015d,
            0.024d);

        var twinkleThreshold = Rig.Capabilities.ColorMode == CameraColorMode.Color ? (byte)208 : (byte)215;
        var twinkleBoostMin = Rig.Capabilities.ColorMode == CameraColorMode.Color ? 6 : 5;
        var twinkleBoostMax = Rig.Capabilities.ColorMode == CameraColorMode.Color ? 27 : 21;

        var brightnessClamped = Math.Clamp(brightnessScale, 0.35d, 3.6d);

        return new SensorResponseProfile(
            BrightnessScale: brightnessClamped,
            LuminanceNoise: luminanceNoise,
            ChrominanceNoise: chromaNoise,
            TwinkleProbability: twinkleProbability,
            TwinkleThreshold: twinkleThreshold,
            TwinkleBoostMin: twinkleBoostMin,
            TwinkleBoostMax: twinkleBoostMax);
    }

    private TimeSpan ComputeSimulatedExposureDuration(ExposureSettings exposure)
    {
        var exposureMs = exposure.ExposureMilliseconds > 0
            ? (double)exposure.ExposureMilliseconds
            : FallbackExposureMilliseconds;

        var gainRatio = NormalizeGain(exposure.Gain);
        var readoutMs = ReadoutOverheadBaseMilliseconds
                        + ReadoutOverheadGainScaleMilliseconds * gainRatio
                        + Math.Min(exposureMs * 0.015d, 35d);
        var jitterMs = Math.Clamp(exposureMs * ExposureJitterFraction, 3d, 120d);
        var jitterOffset = (Random.NextDouble() - 0.5d) * jitterMs;

        var totalMs = exposureMs + readoutMs + jitterOffset;

        if (totalMs < 1d)
        {
            totalMs = 8d;
        }

        return TimeSpan.FromMilliseconds(totalMs);
    }

    private static double NormalizeGain(int gain) => Math.Clamp(gain, 0, 480) / 480d;

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

        if (!string.Equals(rig.Camera.Descriptor.Manufacturer, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return rig;
        }

        return rig with
        {
            Camera = rig.Camera with { Descriptor = CreateDefaultDescriptor() }
        };
    }

    private static CameraDescriptor CreateDefaultDescriptor() => new(
        Manufacturer: "HVO",
        Model: "Mock Fisheye AllSky",
        DriverVersion: "2.0.0",
        AdapterName: nameof(MockCameraAdapter),
        Capabilities: new[] { "Synthetic", "StackingCompatible", "FisheyeProjection" });
}
