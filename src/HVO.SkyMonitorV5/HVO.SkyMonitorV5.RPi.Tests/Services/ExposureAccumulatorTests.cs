#nullable enable

using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Services;

[TestClass]
public sealed class ExposureAccumulatorTests
{
    [TestMethod]
    public void ComputeMetrics_GrayBitmap_ReturnsExpected()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Gray8, SKAlphaType.Opaque));
        using (var pixmap = bitmap.PeekPixels())
        {
            var span = pixmap!.GetPixelSpan();
            span[0] = 80;
        }

        var metrics = ExposureAccumulator.ComputeMetrics(bitmap);

        Assert.AreEqual(80, metrics.AverageLuminance, 1e-6);
        Assert.AreEqual(80, metrics.MinimumLuminance, 1e-6);
        Assert.AreEqual(80, metrics.MaximumLuminance, 1e-6);
        Assert.AreEqual(1, metrics.SampleCount);
    }

    [TestMethod]
    public void ComputeMetrics_BgraBitmap_UsesSampledPixel()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        var sampledColor = new SKColor(10, 20, 30);
        bitmap.SetPixel(0, 0, sampledColor);
        bitmap.SetPixel(1, 0, new SKColor(255, 255, 255));
        bitmap.SetPixel(0, 1, new SKColor(0, 0, 0));
        bitmap.SetPixel(1, 1, new SKColor(120, 120, 120));

        var metrics = ExposureAccumulator.ComputeMetrics(bitmap);
        var expected = (0.2126 * sampledColor.Red) + (0.7152 * sampledColor.Green) + (0.0722 * sampledColor.Blue);

        Assert.AreEqual(expected, metrics.AverageLuminance, 1e-6);
        Assert.AreEqual(expected, metrics.MinimumLuminance, 1e-6);
        Assert.AreEqual(expected, metrics.MaximumLuminance, 1e-6);
        Assert.AreEqual(1, metrics.SampleCount);
    }

    [TestMethod]
    public void ComputeMetrics_InvalidBitmap_ReturnsDefaults()
    {
        using var bitmap = new SKBitmap();

        var metrics = ExposureAccumulator.ComputeMetrics(bitmap);

        Assert.AreEqual(0, metrics.SampleCount);
        Assert.AreEqual(0, metrics.AverageLuminance);
        Assert.AreEqual(0, metrics.MinimumLuminance);
        Assert.AreEqual(0, metrics.MaximumLuminance);
    }
}
