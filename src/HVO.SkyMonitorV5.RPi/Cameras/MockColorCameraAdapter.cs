#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing;

namespace HVO.SkyMonitorV5.RPi.Cameras;

/// <summary>
/// Mock camera adapter that renders the synthetic scene in full colour, applying Bayer-like noise so
/// the pipeline experiences a workload closer to the ASI174MC sensor.
/// </summary>
public sealed class MockColorCameraAdapter : MockCameraAdapter
{
    private readonly ILogger<MockColorCameraAdapter>? _logger;
    private readonly MockColorNoiseProfile _noiseProfile;

    public MockColorCameraAdapter(
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor,
        IOptionsMonitor<StarCatalogOptions> catalogOptions,
        IOptionsMonitor<CardinalDirectionsOptions> cardinalOptions,
        IServiceScopeFactory scopeFactory,
        RigSpec rigSpec,
        IObservatoryClock observatoryClock,
        ILoggerFactory? loggerFactory = null,
        ILogger<MockColorCameraAdapter>? logger = null,
        MockColorNoiseProfile? noiseProfile = null,
        IFramePreprocessingOrchestrator? preprocessingOrchestrator = null)
        : base(
            locationMonitor,
            catalogOptions,
            cardinalOptions,
            scopeFactory,
            rigSpec,
            observatoryClock,
            loggerFactory?.CreateLogger<MockCameraAdapter>() ?? NullLogger<MockCameraAdapter>.Instance,
            preprocessingOrchestrator)
    {
        _logger = logger ?? loggerFactory?.CreateLogger<MockColorCameraAdapter>();
        _noiseProfile = noiseProfile ?? MockColorNoiseProfile.Default;
    }

    protected override void ApplySensorNoise(SKBitmap bitmap, ExposureSettings exposure)
    {
        base.ApplySensorNoise(bitmap, exposure);

        var profile = BuildSensorResponseProfile(exposure);
        _logger?.LogTrace(
            "Applied colour sensor response (exposure {ExposureMs} ms, gain {Gain}, brightness x{Brightness:0.00}, chromaScale {ChromaScale:0.00}).",
            exposure.ExposureMilliseconds,
            exposure.Gain,
            profile.BrightnessScale,
            _noiseProfile.ChromaNoiseScale);
    }

    protected override void ApplyBackgroundNoise(
        in SensorResponseProfile profile,
        Span<byte> span,
        int spanIndex,
        byte alpha,
        double originalBlue,
        double originalGreen,
        double originalRed)
    {
        var brightnessScale = profile.BrightnessScale;

        var scaledBlue = Math.Clamp(originalBlue * brightnessScale, 0d, 255d);
        var scaledGreen = Math.Clamp(originalGreen * brightnessScale, 0d, 255d);
        var scaledRed = Math.Clamp(originalRed * brightnessScale, 0d, 255d);

        var luminanceNoise = (Random.NextDouble() - 0.5d) * 256d * profile.LuminanceNoise;
        var chromaNoiseScale = _noiseProfile.ChromaNoiseScale;
        var chromaNoiseBlue = (Random.NextDouble() - 0.5d) * 256d * profile.ChrominanceNoise * chromaNoiseScale;
        var chromaNoiseRed = (Random.NextDouble() - 0.5d) * 256d * profile.ChrominanceNoise * chromaNoiseScale;

        var greenChromaCompensation = (chromaNoiseBlue + chromaNoiseRed) * _noiseProfile.GreenChromaCompensationFactor;

        var noisyBlue = Math.Clamp(scaledBlue + luminanceNoise + chromaNoiseBlue, 0d, 255d);
        var noisyRed = Math.Clamp(scaledRed + luminanceNoise + chromaNoiseRed, 0d, 255d);
        var noisyGreen = Math.Clamp(scaledGreen + luminanceNoise - greenChromaCompensation, 0d, 255d);

        span[spanIndex] = (byte)Math.Round(noisyBlue);
        span[spanIndex + 1] = (byte)Math.Round(noisyGreen);
        span[spanIndex + 2] = (byte)Math.Round(noisyRed);
        span[spanIndex + 3] = alpha;
    }
}
