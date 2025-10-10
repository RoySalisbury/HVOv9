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
        var luminanceNoiseLevel = Math.Clamp(exposure.Gain / 480d, 0.012d, 0.09d);
        var chromaNoiseLevel = luminanceNoiseLevel * 0.35d;
        var twinkleProbability = 0.0015d + exposure.Gain * 0.000012d;

        var span = bitmap.GetPixelSpan();
        for (var i = 0; i < span.Length; i += 4)
        {
            var alpha = span[i + 3];

            var blue = span[i];
            var green = span[i + 1];
            var red = span[i + 2];

            var luminanceNoise = (int)((Random.NextDouble() - 0.5d) * 512 * luminanceNoiseLevel);
            var chromaNoiseBlue = (int)((Random.NextDouble() - 0.5d) * 512 * chromaNoiseLevel);
            var chromaNoiseRed = (int)((Random.NextDouble() - 0.5d) * 512 * chromaNoiseLevel);

            var twinkleBoost = 0;
            var maxChannel = Math.Max(red, Math.Max(green, blue));
            if (maxChannel > 225 && Random.NextDouble() < twinkleProbability)
            {
                twinkleBoost = Random.Next(8, 26);
            }

            var boostedBlue = (byte)Math.Clamp(blue + luminanceNoise + chromaNoiseBlue + twinkleBoost, 0, 255);
            var boostedGreen = (byte)Math.Clamp(green + luminanceNoise - (chromaNoiseBlue + chromaNoiseRed) * 0.5 + twinkleBoost / 2, 0, 255);
            var boostedRed = (byte)Math.Clamp(red + luminanceNoise + chromaNoiseRed + twinkleBoost, 0, 255);

            span[i] = boostedBlue;
            span[i + 1] = boostedGreen;
            span[i + 2] = boostedRed;
            span[i + 3] = alpha;
        }

        _logger?.LogTrace("Applied colour sensor noise model with gain {Gain}.", exposure.Gain);
    }
}
