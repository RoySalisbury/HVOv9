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

namespace HVO.SkyMonitorV5.RPi.Cameras;

/// <summary>
/// Mock camera adapter that renders the synthetic scene in full colour, applying Bayer-like noise so
/// the pipeline experiences a workload closer to the ASI174MC sensor.
/// </summary>
public sealed class MockColorCameraAdapter : MockCameraAdapter
{
    private readonly ILogger<MockColorCameraAdapter>? _logger;

    public MockColorCameraAdapter(
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor,
        IOptionsMonitor<StarCatalogOptions> catalogOptions,
        IOptionsMonitor<CardinalDirectionsOptions> cardinalOptions,
        IServiceScopeFactory scopeFactory,
        RigSpec rigSpec,
        IObservatoryClock observatoryClock,
        ILoggerFactory? loggerFactory = null,
        ILogger<MockColorCameraAdapter>? logger = null)
        : base(
            locationMonitor,
            catalogOptions,
            cardinalOptions,
            scopeFactory,
            rigSpec,
            observatoryClock,
            loggerFactory?.CreateLogger<MockCameraAdapter>() ?? NullLogger<MockCameraAdapter>.Instance)
    {
        _logger = logger ?? loggerFactory?.CreateLogger<MockColorCameraAdapter>();
    }

    protected override void ApplySensorNoise(SKBitmap bitmap, ExposureSettings exposure)
    {
        var profile = BuildSensorResponseProfile(exposure);

        var span = bitmap.GetPixelSpan();
        for (var i = 0; i < span.Length; i += 4)
        {
            var alpha = span[i + 3];

            var blue = span[i];
            var green = span[i + 1];
            var red = span[i + 2];

            var scaledBlue = Math.Clamp(blue * profile.BrightnessScale, 0d, 255d);
            var scaledGreen = Math.Clamp(green * profile.BrightnessScale, 0d, 255d);
            var scaledRed = Math.Clamp(red * profile.BrightnessScale, 0d, 255d);

            var luminanceNoise = (Random.NextDouble() - 0.5d) * 512d * profile.LuminanceNoise;
            var chromaNoiseBlue = (Random.NextDouble() - 0.5d) * 512d * profile.ChrominanceNoise;
            var chromaNoiseRed = (Random.NextDouble() - 0.5d) * 512d * profile.ChrominanceNoise;

            var twinkleBoost = 0d;
            var maxChannel = Math.Max(scaledRed, Math.Max(scaledGreen, scaledBlue));
            if (maxChannel >= profile.TwinkleThreshold && Random.NextDouble() < profile.TwinkleProbability)
            {
                twinkleBoost = Random.Next(profile.TwinkleBoostMin, profile.TwinkleBoostMax + 1);
            }

            var boostedBlue = (byte)Math.Clamp(scaledBlue + luminanceNoise + chromaNoiseBlue + twinkleBoost, 0d, 255d);
            var boostedRed = (byte)Math.Clamp(scaledRed + luminanceNoise + chromaNoiseRed + twinkleBoost, 0d, 255d);
            var greenChromaCompensation = (chromaNoiseBlue + chromaNoiseRed) * 0.35d;
            var boostedGreen = (byte)Math.Clamp(scaledGreen + luminanceNoise - greenChromaCompensation + twinkleBoost / 2d, 0d, 255d);

            span[i] = boostedBlue;
            span[i + 1] = boostedGreen;
            span[i + 2] = boostedRed;
            span[i + 3] = alpha;
        }

        _logger?.LogTrace(
            "Applied colour sensor response (exposure {ExposureMs} ms, gain {Gain}, brightness x{Brightness:0.00}).",
            exposure.ExposureMilliseconds,
            exposure.Gain,
            profile.BrightnessScale);
    }
}
