#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
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
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;

namespace HVO.SkyMonitorV5.RPi.Cameras;

/// <summary>
/// Synthetic fisheye camera adapter that renders a realistic all-sky projection using the starfield engine.
/// </summary>
[CameraDriver(
    id: CameraDriverIdentifiers.SimulationMockMono,
    DisplayName = "Mock All-Sky (Monochrome)",
    Description = "Synthetic monochrome fisheye adapter used for development and testing.",
    Version = "1.0.0")]
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
    private const double DefaultSensorNoiseScale = 0.06; // baseline synthetic noise amplitude (lowered for less aggressive grain)

    private const string DisableSensorNoiseVariable = "HVO_DISABLE_SENSOR_NOISE";
    private const string SensorNoiseScaleVariable = "HVO_SENSOR_NOISE_SCALE";
    private static bool _sensorNoiseDisabledLogged;
    private static readonly object SensorNoiseLogLock = new();
    private static readonly SKColorSpace LinearSrgbColorSpace = SKColorSpace.CreateSrgbLinear();

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
        ILogger<MockCameraAdapter>? logger = null,
        IFramePreprocessingOrchestrator? preprocessingOrchestrator = null)
        : base(
            EnsureRigDescriptor(rigSpec),
            observatoryClock,
            logger ?? NullLogger<MockCameraAdapter>.Instance,
            preprocessingOrchestrator)
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
        SKSurface? starfieldSurface = null;
        SKBitmap? starfield = null;
        SkiaPixelLease? pixelLease = null;
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

                var delayCompleted = await CancellationTokenHelpers.DelayWithoutThrowAsync(simulatedDuration, cancellationToken).ConfigureAwait(false);
                if (!delayCompleted)
                {
                    return Result<AdapterFrame>.Failure(new OperationCanceledException("Mock camera exposure cancelled.", cancellationToken));
                }
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

            var imageInfo = new SKImageInfo(frameWidth, frameHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            starfield = new SKBitmap(imageInfo);
            if (starfield.GetPixels() == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to allocate starfield bitmap pixels.");
            }

            starfieldSurface = CreateCaptureSurface(imageInfo, starfield);

            engine.RenderOntoSurface(
                starfieldSurface,
                catalogStars,
                planets: planetMarks,
                randomFillerCount: RandomFillerStars,
                randomSeed: null,
                dimFaintStars: true,
                planetOptions: PlanetRenderOptions.Default,
                out _,
                out _);

            starfieldSurface.Canvas?.Flush();
            starfieldSurface.Canvas?.DrawColor(SKColors.Black, SKBlendMode.DstOver);

            var frameTimestamp = captureInstant;
            pixelLease = SkiaPixelLease.FromBitmap(starfield, disposeBitmap: false);

            var adapterFrame = new AdapterFrame(
                starfield,
                PixelLease: pixelLease,
                ImmutableImage: null,
                starfieldSurface,
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
            starfieldSurface = null;
            pixelLease = null;

            return Result<AdapterFrame>.Success(adapterFrame);
        }
        catch (OperationCanceledException ex)
        {
            starfield?.Dispose();
            starfieldSurface?.Dispose();
            engine?.Dispose();
            Logger.LogDebug(ex, "Mock camera capture cancelled.");
            return Result<AdapterFrame>.Failure(ex);
        }
        catch (Exception)
        {
            starfield?.Dispose();
            starfieldSurface?.Dispose();
            throw;
        }
        finally
        {
            engine?.Dispose();
            starfield?.Dispose();
            starfieldSurface?.Dispose();
            pixelLease?.Dispose();
        }
    }

    protected override Task<Result<AdapterFrame>> PostprocessFrameAsync(AdapterFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AdapterFrame workingFrame = frame;

        if (IsSensorNoiseDisabled())
        {
            LogSensorNoiseDisabledOnce();
        }
        else
        {
            ApplySensorNoise(workingFrame.Bitmap, workingFrame.Exposure);
        }

        var updatedFrame = UpdateImmutableSnapshot(workingFrame);
        workingFrame.Surface?.Dispose();
        updatedFrame = updatedFrame with { Surface = null };
        return Task.FromResult(Result<AdapterFrame>.Success(updatedFrame));
    }

    private static SKSurface CreateCaptureSurface(SKImageInfo info, SKBitmap bitmap)
    {
        var pixels = bitmap.GetPixels();
        if (pixels == IntPtr.Zero)
        {
            throw new InvalidOperationException("Bitmap does not expose pixel memory for capture surface creation.");
        }

        var surface = SKSurface.Create(info, pixels, bitmap.RowBytes);
        if (surface is null)
        {
            throw new InvalidOperationException($"Failed to allocate capture surface for dimensions {info.Width}x{info.Height}.");
        }

        return surface;
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

        const double BlueWeight = 0.0722d;
        const double GreenWeight = 0.7152d;
        const double RedWeight = 0.2126d;
    const double StarLuminanceThreshold = 32d;
    const int MaskDilationRadius = 1;

    var width = bitmap.Width;
    var height = bitmap.Height;
    var halfWidth = width / 2;
        var noiseEnabledWidth = width;

        var span = bitmap.GetPixelSpan();
        var originalBuffer = ArrayPool<byte>.Shared.Rent(span.Length);
        var original = originalBuffer.AsSpan(0, span.Length);
        span.CopyTo(original);

        var pixelCount = width * height;

        var starMaskBuffer = ArrayPool<byte>.Shared.Rent(pixelCount);
        var starMask = starMaskBuffer.AsSpan(0, pixelCount);
        starMask.Clear();

        try
        {
            // First pass: classify bright pixels as stars.
            for (var y = 0; y < height; y++)
            {
                var rowOffset = y * width;
                for (var x = 0; x < width; x++)
                {
                    var pixelIndex = rowOffset + x;
                    var spanIndex = pixelIndex * 4;

                    var red = original[spanIndex + 2];
                    var green = original[spanIndex + 1];
                    var blue = original[spanIndex];

                    var luminance = (RedWeight * red) + (GreenWeight * green) + (BlueWeight * blue);
                    if (luminance >= StarLuminanceThreshold)
                    {
                        starMask[pixelIndex] = 1;
                    }
                }
            }

            // Dilate star mask slightly so noise stays away from star halos.
            if (MaskDilationRadius > 0)
            {
                var dilatedBuffer = ArrayPool<byte>.Shared.Rent(pixelCount);
                var dilated = dilatedBuffer.AsSpan(0, pixelCount);
                starMask.CopyTo(dilated);

                for (var y = 0; y < height; y++)
                {
                    var rowOffset = y * width;
                    for (var x = 0; x < width; x++)
                    {
                        var pixelIndex = rowOffset + x;
                        if (starMask[pixelIndex] == 0)
                        {
                            continue;
                        }

                        for (var dy = -MaskDilationRadius; dy <= MaskDilationRadius; dy++)
                        {
                            var ny = y + dy;
                            if (ny < 0 || ny >= height)
                            {
                                continue;
                            }

                            var neighborRowOffset = ny * width;
                            for (var dx = -MaskDilationRadius; dx <= MaskDilationRadius; dx++)
                            {
                                var nx = x + dx;
                                if (nx < 0 || nx >= width)
                                {
                                    continue;
                                }

                                dilated[neighborRowOffset + nx] = 1;
                            }
                        }
                    }
                }

                ArrayPool<byte>.Shared.Return(starMaskBuffer);
                starMaskBuffer = dilatedBuffer;
                starMask = dilated;
            }

            // Second pass: apply noise or preserve stars based on the mask.
            for (var y = 0; y < height; y++)
            {
                var rowOffset = y * width;
                for (var x = 0; x < width; x++)
                {
                    var pixelIndex = rowOffset + x;
                    var spanIndex = pixelIndex * 4;

                    if (x >= noiseEnabledWidth)
                    {
                        span[spanIndex] = original[spanIndex];
                        span[spanIndex + 1] = original[spanIndex + 1];
                        span[spanIndex + 2] = original[spanIndex + 2];
                        span[spanIndex + 3] = original[spanIndex + 3];
                        continue;
                    }

                    var alpha = original[spanIndex + 3];
                    if (alpha == 0)
                    {
                        span[spanIndex] = span[spanIndex + 1] = span[spanIndex + 2] = 0;
                        span[spanIndex + 3] = 0;
                        continue;
                    }

                    var originalBlue = (double)original[spanIndex];
                    var originalGreen = (double)original[spanIndex + 1];
                    var originalRed = (double)original[spanIndex + 2];

                    if (starMask[pixelIndex] != 0)
                    {
                        var originalLuminance = (RedWeight * originalRed) + (GreenWeight * originalGreen) + (BlueWeight * originalBlue);
                        var twinkleScale = 1d;

                        if (originalLuminance >= profile.TwinkleThreshold && Random.NextDouble() < profile.TwinkleProbability)
                        {
                            var boost = Random.Next(profile.TwinkleBoostMin, profile.TwinkleBoostMax + 1);
                            twinkleScale += boost / 255d;
                        }

                        var scaledBlue = originalBlue * twinkleScale;
                        var scaledGreen = originalGreen * twinkleScale;
                        var scaledRed = originalRed * twinkleScale;

                        var luminanceNoise = (Random.NextDouble() - 0.5d) * profile.LuminanceNoise * 24d;
                        scaledBlue += luminanceNoise;
                        scaledGreen += luminanceNoise;
                        scaledRed += luminanceNoise;

                        var maxChannel = Math.Max(scaledBlue, Math.Max(scaledGreen, scaledRed));
                        if (maxChannel > 255d && maxChannel > 0d)
                        {
                            var compress = 255d / maxChannel;
                            scaledBlue *= compress;
                            scaledGreen *= compress;
                            scaledRed *= compress;
                        }

                        span[spanIndex] = (byte)Math.Clamp(Math.Round(scaledBlue), 0d, 255d);
                        span[spanIndex + 1] = (byte)Math.Clamp(Math.Round(scaledGreen), 0d, 255d);
                        span[spanIndex + 2] = (byte)Math.Clamp(Math.Round(scaledRed), 0d, 255d);
                        span[spanIndex + 3] = alpha;
                        continue;
                    }

                    ApplyBackgroundNoise(
                        profile,
                        span,
                        spanIndex,
                        alpha,
                        originalBlue,
                        originalGreen,
                        originalRed);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(originalBuffer);
            ArrayPool<byte>.Shared.Return(starMaskBuffer);
        }

        Logger.LogTrace(
            "Applied synthetic sensor response (exposure {ExposureMs} ms, gain {Gain}, brightness x{Brightness:0.00}, noise {Noise:0.000}).",
            exposure.ExposureMilliseconds,
            exposure.Gain,
            profile.BrightnessScale,
            profile.LuminanceNoise);
    }

    protected virtual void ApplyBackgroundNoise(
        in SensorResponseProfile profile,
        Span<byte> span,
        int spanIndex,
        byte alpha,
        double originalBlue,
        double originalGreen,
        double originalRed)
    {
        var baseLift = profile.LuminanceNoise * 14d;
        var noiseAmplitude = profile.LuminanceNoise * 44d;

        var blueNoise = baseLift + (Random.NextDouble() - 0.5d) * noiseAmplitude;
        var greenNoise = baseLift * 0.85d + (Random.NextDouble() - 0.5d) * noiseAmplitude * 0.6d;
        var redNoise = baseLift * 0.7d + (Random.NextDouble() - 0.5d) * noiseAmplitude * 0.5d;

        if (profile.ChrominanceNoise > 0d)
        {
            var chromaAmplitude = profile.ChrominanceNoise * 20d;
            blueNoise += (Random.NextDouble() - 0.5d) * chromaAmplitude;
            greenNoise += (Random.NextDouble() - 0.5d) * chromaAmplitude * 0.7d;
            redNoise += (Random.NextDouble() - 0.5d) * chromaAmplitude;
        }

        var newBlue = Math.Clamp(originalBlue + blueNoise, 0d, 90d);
        var newGreen = Math.Clamp(originalGreen + greenNoise, 0d, 75d);
        var newRed = Math.Clamp(originalRed + redNoise, 0d, 70d);

        span[spanIndex] = (byte)Math.Round(newBlue);
        span[spanIndex + 1] = (byte)Math.Round(newGreen);
        span[spanIndex + 2] = (byte)Math.Round(newRed);
        span[spanIndex + 3] = alpha;
    }

    private static AdapterFrame UpdateImmutableSnapshot(AdapterFrame frame)
    {
        frame.ImmutableImage?.Dispose();

        var immutable = CreateImmutableSnapshot(frame.Bitmap);
        return frame with { ImmutableImage = immutable };
    }

    private static SKImage? CreateImmutableSnapshot(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels();
        if (pixmap is null)
        {
            return null;
        }

        var info = pixmap.Info.WithColorSpace(LinearSrgbColorSpace);
        using var linearPixmap = new SKPixmap(info, pixmap.GetPixels(), pixmap.RowBytes);
        return SKImage.FromPixels(linearPixmap);
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

        luminanceNoise *= ResolveSensorNoiseScale();

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

    protected virtual double ResolveSensorNoiseScale()
    {
        var raw = Environment.GetEnvironmentVariable(SensorNoiseScaleVariable);
        if (!string.IsNullOrWhiteSpace(raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return Math.Clamp(parsed, 0d, 2d);
        }

        return DefaultSensorNoiseScale;
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
