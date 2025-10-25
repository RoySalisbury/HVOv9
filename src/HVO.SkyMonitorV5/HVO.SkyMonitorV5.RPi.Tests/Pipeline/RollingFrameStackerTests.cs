using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Pipeline;

[TestClass]
public sealed class RollingFrameStackerTests
{
    [TestMethod]
    public void Accumulate_WhenStackingDisabled_ReturnsFrameContextWithoutDisposing()
    {
        using var surfacePool = new SkiaSurfacePool();
        using var captureBitmap = new SKBitmap(width: 8, height: 8);
        var (context, wasDisposed) = CreateFrameContext();
        var exposure = new ExposureSettings(500, 200, false, false);
        var capture = new CapturedImage(context.FrameId, captureBitmap, DateTimeOffset.UtcNow, exposure, context);

        var stacker = new RollingFrameStacker(surfacePool);
        var configuration = new CameraConfiguration(
            EnableStacking: false,
            StackingFrameCount: 1,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 1,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

        var result = stacker.Accumulate(capture, configuration);

        Assert.AreSame(context, result.Context, "Frame context should be forwarded when stacking is disabled.");
        Assert.IsFalse(wasDisposed(), "Stacker should not dispose the frame context.");

        DisposeFrameResult(result);
        context.Dispose();
        stacker.Dispose();
    }

    [TestMethod]
    public void Accumulate_WithStackingEnabled_UsesSharedContextAcrossStackedFrames()
    {
        using var surfacePool = new SkiaSurfacePool();
        var stacker = new RollingFrameStacker(surfacePool);
        var configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 2,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 2,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

        var (context, wasDisposed) = CreateFrameContext();
        var exposure = new ExposureSettings(500, 200, false, false);

        using var firstBitmap = new SKBitmap(width: 8, height: 8);
        var firstCapture = new CapturedImage(context.FrameId, firstBitmap, DateTimeOffset.UtcNow, exposure, context);
        var firstResult = stacker.Accumulate(firstCapture, configuration);
        DisposeFrameResult(firstResult);

        using var secondBitmap = new SKBitmap(width: 8, height: 8);
        var secondCapture = new CapturedImage(context.FrameId, secondBitmap, DateTimeOffset.UtcNow.AddMilliseconds(200), exposure, context);
        var stackedResult = stacker.Accumulate(secondCapture, configuration);

        Assert.AreSame(context, stackedResult.Context, "Stacked frame should retain the original frame context instance.");
        Assert.AreEqual(2, stackedResult.FramesStacked, "Stacked frame count should reflect the number of frames combined.");
        Assert.IsFalse(wasDisposed(), "Frame context should remain undisposed until the pipeline is finished.");

        DisposeFrameResult(stackedResult);
        context.Dispose();
        stacker.Dispose();
    }

    [TestMethod]
    public void Accumulate_PartialStacksIncreaseUntilTarget()
    {
        using var surfacePool = new SkiaSurfacePool();
        var stacker = new RollingFrameStacker(surfacePool);
        var configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 4,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 4,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

        var exposure = new ExposureSettings(1_000, 200, false, false);
        var expectedCounts = new[] { 1, 2, 3, 4 };
        var expectedIntegration = new[] { 1_000, 2_000, 3_000, 4_000 };
        var observedCounts = new List<int>(expectedCounts.Length);

        for (var i = 0; i < expectedCounts.Length; i++)
        {
            var bitmap = new SKBitmap(width: 8, height: 8);
            var capture = new CapturedImage(Guid.NewGuid(), bitmap, DateTimeOffset.UtcNow.AddMilliseconds(i * 10), exposure, null);
            var result = stacker.Accumulate(capture, configuration);

            observedCounts.Add(result.FramesStacked);
            Assert.AreEqual(expectedIntegration[i], result.IntegrationMilliseconds, "Integration should scale with the number of stacked frames.");

            DisposeFrameResult(result);
        }

        CollectionAssert.AreEqual(expectedCounts, observedCounts, "Stacked frame count should increase as the buffer fills.");
        stacker.Dispose();
    }

    [TestMethod]
    [DataRow(SKColorType.Rgba8888)]
    [DataRow(SKColorType.Bgra8888)]
    public void Accumulate_WithLinear8BitFrames_PreservesExpectedAverage(SKColorType colorType)
    {
        using var surfacePool = new SkiaSurfacePool();
        var stacker = new RollingFrameStacker(surfacePool);
        var configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 2,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 2,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        const int width = 6;
        const int height = 4;
        const float redScaleA = 0.35f;
        const float greenScaleA = 0.65f;
        const float blueValueA = 0.25f;
        const float redScaleB = 0.82f;
        const float greenScaleB = 0.28f;
        const float blueValueB = 0.73f;

        var immutableA = SkiaTestImageFactory.CreateLinearGradientImage(width, height, colorType, SKAlphaType.Premul, colorSpace, redScaleA, greenScaleA, blueValueA);
        var immutableB = SkiaTestImageFactory.CreateLinearGradientImage(width, height, colorType, SKAlphaType.Premul, colorSpace, redScaleB, greenScaleB, blueValueB);
        var firstData = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(immutableA, colorType);
        var secondData = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(immutableB, colorType);

        var bitmapA = SkiaTestImageFactory.CreateLinearGradientBitmap(width, height, colorType, SKAlphaType.Premul, colorSpace, redScaleA, greenScaleA, blueValueA);
        var bitmapB = SkiaTestImageFactory.CreateLinearGradientBitmap(width, height, colorType, SKAlphaType.Premul, colorSpace, redScaleB, greenScaleB, blueValueB);

        var exposure = new ExposureSettings(1_000, 200, false, false);

        var captureA = new CapturedImage(Guid.NewGuid(), bitmapA, DateTimeOffset.UtcNow, exposure, null)
        {
            ImmutableImage = immutableA
        };

        var captureB = new CapturedImage(Guid.NewGuid(), bitmapB, DateTimeOffset.UtcNow.AddMilliseconds(250), exposure, null)
        {
            ImmutableImage = immutableB
        };

        var partialResult = stacker.Accumulate(captureA, configuration);
        DisposeFrameResult(partialResult);

        var stackedResult = stacker.Accumulate(captureB, configuration);

        Assert.AreEqual(2, stackedResult.FramesStacked, "Stacker should report two frames combined.");

        var stackedData = SkiaTestImageFactory.GetFloatPixelBuffer(stackedResult.StackedImmutableImage!);

        for (var i = 0; i < stackedData.Length; i++)
        {
            var expectedIndex = i;
            if (colorType == SKColorType.Bgra8888)
            {
                var channel = i % 4;
                if (channel == 0)
                {
                    expectedIndex = i + 2;
                }
                else if (channel == 2)
                {
                    expectedIndex = i - 2;
                }
            }

            var expected = (firstData[expectedIndex] + secondData[expectedIndex]) * 0.5f;
            Assert.AreEqual(expected, stackedData[i], 1e-3f, $"Averaged channel {i} did not match expectation for {colorType} input.");
        }

        DisposeFrameResult(stackedResult);
        stacker.Dispose();
    }

    [TestMethod]
    public void Accumulate_WithSrgb8BitFrames_AppliesGammaAwareAverage()
    {
        using var surfacePool = new SkiaSurfacePool();
        var stacker = new RollingFrameStacker(surfacePool);
        var configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 2,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 2,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

        using var srgbSpace = SKColorSpace.CreateSrgb();
        const int width = 6;
        const int height = 4;

        using var immutableA = SkiaTestImageFactory.CreateLinearGradientImage(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, srgbSpace, redScale: 0.24f, greenScale: 0.66f, blueValue: 0.35f);
        using var immutableB = SkiaTestImageFactory.CreateLinearGradientImage(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, srgbSpace, redScale: 0.78f, greenScale: 0.31f, blueValue: 0.62f);

        var bytesA = SkiaTestImageFactory.GetBytePixelBuffer(immutableA, SKColorType.Rgba8888);
        var bytesB = SkiaTestImageFactory.GetBytePixelBuffer(immutableB, SKColorType.Rgba8888);

        var expected = new float[bytesA.Length];
        for (var i = 0; i < expected.Length; i++)
        {
            var normalizedA = bytesA[i] / 255f;
            var normalizedB = bytesB[i] / 255f;
            if (i % 4 == 3)
            {
                expected[i] = (normalizedA + normalizedB) * 0.5f;
            }
            else
            {
                var linearA = SkiaTestImageFactory.ConvertSrgbToLinear(normalizedA);
                var linearB = SkiaTestImageFactory.ConvertSrgbToLinear(normalizedB);
                expected[i] = (linearA + linearB) * 0.5f;
            }
        }

        var bitmapA = SkiaTestImageFactory.CreateLinearGradientBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, srgbSpace, redScale: 0.24f, greenScale: 0.66f, blueValue: 0.35f);
        var bitmapB = SkiaTestImageFactory.CreateLinearGradientBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, srgbSpace, redScale: 0.78f, greenScale: 0.31f, blueValue: 0.62f);

        var exposure = new ExposureSettings(1_000, 200, false, false);

        var captureA = new CapturedImage(Guid.NewGuid(), bitmapA, DateTimeOffset.UtcNow, exposure, null)
        {
            ImmutableImage = immutableA
        };

        var captureB = new CapturedImage(Guid.NewGuid(), bitmapB, DateTimeOffset.UtcNow.AddMilliseconds(250), exposure, null)
        {
            ImmutableImage = immutableB
        };

        var partialResult = stacker.Accumulate(captureA, configuration);
        DisposeFrameResult(partialResult);

        var stackedResult = stacker.Accumulate(captureB, configuration);

        Assert.AreEqual(2, stackedResult.FramesStacked, "Stacker should report two frames combined for sRGB inputs.");

        var actual = SkiaTestImageFactory.GetFloatPixelBuffer(stackedResult.StackedImmutableImage!);
        const float tolerance = 1e-3f;
        Assert.HasCount(expected.Length, actual, "Expected buffer length should match actual buffer length.");

        for (var i = 0; i < expected.Length; i++)
        {
            var delta = Math.Abs(expected[i] - actual[i]);
            if (delta > tolerance)
            {
                Assert.Fail($"Gamma-aware averaging deviated by {delta:F4} at index {i}. Expected {expected[i]:F4}, actual {actual[i]:F4}.");
            }
        }

        DisposeFrameResult(stackedResult);
        stacker.Dispose();
    }

    [TestMethod]
    public void Accumulate_WithMonochromeF16Frames_ProducesExpectedAverage()
    {
        using var surfacePool = new SkiaSurfacePool();
        var stacker = new RollingFrameStacker(surfacePool);
        var configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 2,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 2,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

        const int width = 6;
        const int height = 4;

        using var immutableA = SkiaTestImageFactory.CreateMonochromeGradientImage(width, height, minValue: 0.18f, maxValue: 0.74f);
        using var immutableB = SkiaTestImageFactory.CreateMonochromeGradientImage(width, height, minValue: 0.33f, maxValue: 0.92f);

        var firstData = SkiaTestImageFactory.GetFloatPixelBuffer(immutableA);
        var secondData = SkiaTestImageFactory.GetFloatPixelBuffer(immutableB);

        var bitmapA = SkiaImageUtilities.CreateBitmapCopy(immutableA);
        var bitmapB = SkiaImageUtilities.CreateBitmapCopy(immutableB);

        var exposure = new ExposureSettings(1_000, 200, false, false);

        var captureA = new CapturedImage(Guid.NewGuid(), bitmapA, DateTimeOffset.UtcNow, exposure, null)
        {
            ImmutableImage = immutableA
        };

        var captureB = new CapturedImage(Guid.NewGuid(), bitmapB, DateTimeOffset.UtcNow.AddMilliseconds(250), exposure, null)
        {
            ImmutableImage = immutableB
        };

        var partialResult = stacker.Accumulate(captureA, configuration);
        DisposeFrameResult(partialResult);

        var stackedResult = stacker.Accumulate(captureB, configuration);
        Assert.AreEqual(2, stackedResult.FramesStacked, "Stacker should report two frames combined for monochrome inputs.");

        var stackedData = SkiaTestImageFactory.GetFloatPixelBuffer(stackedResult.StackedImmutableImage!);
        const float tolerance = 1e-3f;

        for (var i = 0; i < stackedData.Length; i += 4)
        {
            var expected = (firstData[i] + secondData[i]) * 0.5f;
            var actualR = stackedData[i];
            var actualG = stackedData[i + 1];
            var actualB = stackedData[i + 2];

            Assert.IsLessThanOrEqualTo(tolerance, Math.Abs(expected - actualR), $"Monochrome R deviation {Math.Abs(expected - actualR):F4} at pixel {i / 4}.");
            Assert.IsLessThanOrEqualTo(tolerance, Math.Abs(expected - actualG), $"Monochrome G deviation {Math.Abs(expected - actualG):F4} at pixel {i / 4}.");
            Assert.IsLessThanOrEqualTo(tolerance, Math.Abs(expected - actualB), $"Monochrome B deviation {Math.Abs(expected - actualB):F4} at pixel {i / 4}.");

            Assert.IsLessThanOrEqualTo(tolerance, Math.Abs(actualR - actualG), $"Monochrome stacked R/G mismatch {Math.Abs(actualR - actualG):F4} at pixel {i / 4}.");
            Assert.IsLessThanOrEqualTo(tolerance, Math.Abs(actualR - actualB), $"Monochrome stacked R/B mismatch {Math.Abs(actualR - actualB):F4} at pixel {i / 4}.");
        }

        DisposeFrameResult(stackedResult);
        stacker.Dispose();
    }

    [TestMethod]
    public void Accumulate_WithHighBitFrames_ProducesExpectedAverage()
    {
        using var surfacePool = new SkiaSurfacePool();
        var stacker = new RollingFrameStacker(surfacePool);
        var configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 2,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 2,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

        using var immutableA = SkiaTestImageFactory.CreateLinearGradientImage(6, 4, redScale: 0.25f, greenScale: 0.5f, blueValue: 0.2f);
        using var immutableB = SkiaTestImageFactory.CreateLinearGradientImage(6, 4, redScale: 0.75f, greenScale: 0.3f, blueValue: 0.8f);

        var bitmapA = SkiaImageUtilities.CreateBitmapCopy(immutableA);
        var bitmapB = SkiaImageUtilities.CreateBitmapCopy(immutableB);

        var exposure = new ExposureSettings(1_000, 200, false, false);

        var captureA = new CapturedImage(Guid.NewGuid(), bitmapA, DateTimeOffset.UtcNow, exposure, null)
        {
            ImmutableImage = immutableA
        };

        var captureB = new CapturedImage(Guid.NewGuid(), bitmapB, DateTimeOffset.UtcNow.AddMilliseconds(250), exposure, null)
        {
            ImmutableImage = immutableB
        };

        var partialResult = stacker.Accumulate(captureA, configuration);
        DisposeFrameResult(partialResult);

        var stackedResult = stacker.Accumulate(captureB, configuration);

        Assert.AreEqual(2, stackedResult.FramesStacked, "Stacker should report two frames combined.");

        var stackedData = SkiaTestImageFactory.GetFloatPixelBuffer(stackedResult.StackedImmutableImage!);
        var firstData = SkiaTestImageFactory.GetFloatPixelBuffer(immutableA);
        var secondData = SkiaTestImageFactory.GetFloatPixelBuffer(immutableB);

        for (var i = 0; i < stackedData.Length; i++)
        {
            var expected = (firstData[i] + secondData[i]) * 0.5f;
            Assert.AreEqual(expected, stackedData[i], 1e-3f, $"Averaged channel {i} did not match expectation.");
        }

        DisposeFrameResult(stackedResult);
        stacker.Dispose();
    }

    private static (FrameContext Context, Func<bool> WasDisposed) CreateFrameContext()
    {
        var rig = RigPresets.MockAsi174_Fujinon;
        var timestamp = DateTimeOffset.UtcNow;
        var engine = new StarFieldEngine(rig, TestLatitude, TestLongitude, timestamp.UtcDateTime, flipHorizontal: false, applyRefraction: true, horizonPadding: 0.95);
        var disposed = false;
        var context = new FrameContext(
            Guid.NewGuid(),
            rig,
            engine,
            timestamp,
            TestLatitude,
            TestLongitude,
            FlipHorizontal: false,
            HorizonPadding: 0.95,
            ApplyRefraction: true,
            DisposeAction: _ => disposed = true);
        return (context, () => disposed);
    }

    private static void DisposeFrameResult(FrameStackResult result)
    {
        result.StackedImage.Dispose();
        result.StackedImmutableImage?.Dispose();
        if (!ReferenceEquals(result.StackedImage, result.OriginalImage))
        {
            result.OriginalImage.Dispose();
        }
        result.OriginalImmutableImage?.Dispose();
    }

    private const double TestLatitude = 35.1987;
    private const double TestLongitude = -114.0539;
}
