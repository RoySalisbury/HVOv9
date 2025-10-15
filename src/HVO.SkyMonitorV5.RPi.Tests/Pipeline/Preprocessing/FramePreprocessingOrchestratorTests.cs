using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Pipeline.Preprocessing;

[TestClass]
public sealed class FramePreprocessingOrchestratorTests
{
    private const int TestWidth = 8;
    private const int TestHeight = 6;
    private const double TestLatitude = 35.1987;
    private const double TestLongitude = -114.0539;

    [TestMethod]
    public async Task ProcessAsync_WithImmutableImage_PreservesLinearDataAndBitDepth()
    {
        using var surfacePool = new SkiaSurfacePool();
        var orchestrator = new FramePreprocessingOrchestrator(surfacePool, NullLogger<FramePreprocessingOrchestrator>.Instance);

        using var immutableSource = SkiaTestImageFactory.CreateLinearGradientImage(TestWidth, TestHeight, redScale: 0.6f, greenScale: 0.8f, blueValue: 0.3f);
        var expected = SkiaTestImageFactory.GetHalfPixelBuffer(immutableSource);
        var sourceBitmap = SkiaImageUtilities.CreateBitmapCopy(immutableSource, SKColorType.RgbaF16, SKAlphaType.Premul);

        var (frame, engine) = CreateAdapterFrame(sourceBitmap, immutableSource, pixelLease: null, surface: null);

        var result = await orchestrator.ProcessAsync(frame, CancellationToken.None);
        Assert.IsTrue(result.IsSuccessful, "Preprocessing should succeed for simple immutable inputs.");

        var processed = result.Value;
        Assert.IsNotNull(processed.ImmutableImage, "Immutable image should be materialized.");
        Assert.AreNotSame(immutableSource, processed.ImmutableImage, "Processed image should be a distinct instance.");

        Assert.AreNotEqual(IntPtr.Zero, processed.ImmutableImage!.Handle, "Processed immutable image should remain valid after preprocessing.");

        var processedPixmap = processed.ImmutableImage!.PeekPixels();
        if (processedPixmap is null)
        {
            Assert.Fail("Processed image must expose pixels.");
        }

        try
        {
            Assert.AreEqual(SKColorType.RgbaF16, processedPixmap.ColorType, "Processed immutable image should retain high-bit color type.");

            var actual = SkiaTestImageFactory.GetHalfPixelBuffer(processed.ImmutableImage);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], actual[i], $"Pixel mismatch at index {i}.");
            }
        }
        finally
        {
            processedPixmap.Dispose();
        }

        Assert.IsNotNull(processed.PixelLease, "Processed frame should include a pixel lease.");
        Assert.IsNotNull(processed.Bitmap, "Processed frame should include a CPU bitmap.");
        Assert.AreEqual(SKColorType.Bgra8888, processed.Bitmap.ColorType, "Processed bitmap should use the CPU-friendly color type.");

        processed.PixelLease!.Dispose();
        processed.ImmutableImage.Dispose();
        processed.Bitmap.Dispose();
        engine.Dispose();
    }

    [TestMethod]
    public async Task ProcessAsync_WithPixelLease_DisposesOriginalLeaseAndProvidesFreshLease()
    {
        using var surfacePool = new SkiaSurfacePool();
        var orchestrator = new FramePreprocessingOrchestrator(surfacePool, NullLogger<FramePreprocessingOrchestrator>.Instance);

        var bitmap = SkiaTestImageFactory.CreateLinearGradientBitmap(TestWidth, TestHeight, redScale: 0.4f, greenScale: 0.9f, blueValue: 0.2f);
        var info = bitmap.Info;
        var pixels = bitmap.GetPixels();
        Assert.AreNotEqual(IntPtr.Zero, pixels, "Bitmap should expose pixel memory for leasing.");

        var leaseDisposed = false;
        var originalLease = SkiaPixelLease.FromPixels(info, pixels, bitmap.RowBytes, () => leaseDisposed = true);

        var (frame, engine) = CreateAdapterFrame(bitmap, immutable: null, pixelLease: originalLease, surface: null);

        var result = await orchestrator.ProcessAsync(frame, CancellationToken.None);
        Assert.IsTrue(result.IsSuccessful, "Preprocessing should succeed when sourcing from a pixel lease.");
        Assert.IsTrue(leaseDisposed, "Original pixel lease should be disposed during preprocessing.");

        var processed = result.Value;
        Assert.IsNotNull(processed.PixelLease, "Processed frame should include a new pixel lease.");
        Assert.AreNotSame(originalLease, processed.PixelLease, "Returned lease should be freshly created.");
        Assert.IsNotNull(processed.Bitmap, "Processed frame should carry a new bitmap instance.");
        Assert.AreNotSame(bitmap, processed.Bitmap, "Original bitmap should be replaced with processed output.");

        var processedSpan = processed.PixelLease!.GetPixelSpan();
        Assert.AreEqual(processed.Bitmap.RowBytes, processed.PixelLease.Pixmap.RowBytes, "Lease stride should match bitmap stride.");
        Assert.IsTrue(processedSpan.Length > 0, "Processed pixel lease should expose pixel data.");

        processed.PixelLease.Dispose();
        processed.ImmutableImage?.Dispose();
        processed.Bitmap.Dispose();
        engine.Dispose();

        originalLease.Dispose();
    }

    [TestMethod]
    public async Task ProcessAsync_WithLinearRgba8888Input_PreservesColorWithinTolerance()
    {
        await AssertLinear8BitColorRoundTripAsync(SKColorType.Rgba8888);
    }

    [TestMethod]
    public async Task ProcessAsync_WithLinearBgra8888Input_PreservesColorWithinTolerance()
    {
        await AssertLinear8BitColorRoundTripAsync(SKColorType.Bgra8888);
    }

    [TestMethod]
    public async Task ProcessAsync_WithSrgbRgba8888Input_LinearizesPixels()
    {
        using var surfacePool = new SkiaSurfacePool();
        var orchestrator = new FramePreprocessingOrchestrator(surfacePool, NullLogger<FramePreprocessingOrchestrator>.Instance);

        using var colorSpace = SKColorSpace.CreateSrgb();
        using var immutableSource = SkiaTestImageFactory.CreateLinearGradientImage(TestWidth, TestHeight, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace, redScale: 0.68f, greenScale: 0.52f, blueValue: 0.41f);
        var bitmap = SkiaTestImageFactory.CreateLinearGradientBitmap(TestWidth, TestHeight, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace, redScale: 0.68f, greenScale: 0.52f, blueValue: 0.41f);

        var baselineBytes = SkiaTestImageFactory.GetBytePixelBuffer(immutableSource, SKColorType.Rgba8888);
        var expectedLinear = new float[baselineBytes.Length];
        for (var i = 0; i < baselineBytes.Length; i++)
        {
            var normalized = baselineBytes[i] / 255f;
            expectedLinear[i] = (i % 4 == 3)
                ? normalized
                : SkiaTestImageFactory.ConvertSrgbToLinear(normalized);
        }

        var (frame, engine) = CreateAdapterFrame(bitmap, immutableSource, pixelLease: null, surface: null);

        var result = await orchestrator.ProcessAsync(frame, CancellationToken.None);
        Assert.IsTrue(result.IsSuccessful, "Preprocessing should succeed with sRGB RGBA8888 inputs.");

        var processed = result.Value;
        Assert.IsNotNull(processed.ImmutableImage, "Processed immutable image should be present.");

        var processedFloats = SkiaTestImageFactory.GetFloatPixelBuffer(processed.ImmutableImage!);
        Assert.AreEqual(expectedLinear.Length, processedFloats.Length, "Expected linearized buffer length must match processed buffer length.");

        const float tolerance = 1e-3f;
        for (var i = 0; i < expectedLinear.Length; i++)
        {
            var delta = Math.Abs(expectedLinear[i] - processedFloats[i]);
            if (delta > tolerance)
            {
                Assert.Fail($"sRGB linearization deviated by {delta:F4} at index {i}. Expected {expectedLinear[i]:F4}, actual {processedFloats[i]:F4}.");
            }
        }

        processed.PixelLease?.Dispose();
        processed.ImmutableImage?.Dispose();
        processed.Bitmap.Dispose();
        engine.Dispose();
    }

    [TestMethod]
    public async Task ProcessAsync_WithMonochromeF16Input_PreservesLuminanceAcrossChannels()
    {
        using var surfacePool = new SkiaSurfacePool();
        var orchestrator = new FramePreprocessingOrchestrator(surfacePool, NullLogger<FramePreprocessingOrchestrator>.Instance);

        using var immutableSource = SkiaTestImageFactory.CreateMonochromeGradientImage(TestWidth, TestHeight, minValue: 0.12f, maxValue: 0.86f);
        var expected = SkiaTestImageFactory.GetFloatPixelBuffer(immutableSource);
        var bitmap = SkiaImageUtilities.CreateBitmapCopy(immutableSource, SKColorType.RgbaF16, SKAlphaType.Premul);

        var (frame, engine) = CreateAdapterFrame(bitmap, immutableSource, pixelLease: null, surface: null);

        var result = await orchestrator.ProcessAsync(frame, CancellationToken.None);
        Assert.IsTrue(result.IsSuccessful, "Preprocessing should succeed with monochrome F16 inputs.");

        var processed = result.Value;
        Assert.IsNotNull(processed.ImmutableImage, "Processed immutable image should be produced.");

        var actual = SkiaTestImageFactory.GetFloatPixelBuffer(processed.ImmutableImage!);
        Assert.AreEqual(expected.Length, actual.Length, "Expected buffer length must match processed buffer length.");

        const float tolerance = 1e-3f;
        for (var i = 0; i < actual.Length; i += 4)
        {
            var expectedValue = expected[i];
            var actualR = actual[i];
            var actualG = actual[i + 1];
            var actualB = actual[i + 2];

            Assert.IsTrue(Math.Abs(expectedValue - actualR) <= tolerance, $"Monochrome channel R deviated by {Math.Abs(expectedValue - actualR):F4} at pixel {i / 4}.");
            Assert.IsTrue(Math.Abs(expectedValue - actualG) <= tolerance, $"Monochrome channel G deviated by {Math.Abs(expectedValue - actualG):F4} at pixel {i / 4}.");
            Assert.IsTrue(Math.Abs(expectedValue - actualB) <= tolerance, $"Monochrome channel B deviated by {Math.Abs(expectedValue - actualB):F4} at pixel {i / 4}.");

            Assert.IsTrue(Math.Abs(actualR - actualG) <= tolerance, $"Monochrome R/G mismatch {Math.Abs(actualR - actualG):F4} at pixel {i / 4}.");
            Assert.IsTrue(Math.Abs(actualR - actualB) <= tolerance, $"Monochrome R/B mismatch {Math.Abs(actualR - actualB):F4} at pixel {i / 4}.");
        }

        processed.PixelLease?.Dispose();
        processed.ImmutableImage?.Dispose();
        processed.Bitmap.Dispose();
        engine.Dispose();
    }

    [TestMethod]
    public async Task ProcessAsync_WithExistingSurface_ReturnsFrameUnchanged()
    {
        using var surfacePool = new SkiaSurfacePool();
        var orchestrator = new FramePreprocessingOrchestrator(surfacePool, NullLogger<FramePreprocessingOrchestrator>.Instance);

        var bitmap = SkiaTestImageFactory.CreateLinearGradientBitmap(TestWidth, TestHeight);
        using var surfaceLease = surfacePool.RentLinearSurface(TestWidth, TestHeight);
        var surface = surfaceLease.Surface;

        var (frame, engine) = CreateAdapterFrame(bitmap, immutable: null, pixelLease: null, surface: surface);

        var result = await orchestrator.ProcessAsync(frame, CancellationToken.None);
        Assert.IsTrue(result.IsSuccessful, "Preprocessing should succeed when supplied surface is provided by adapter.");
        Assert.AreSame(frame, result.Value, "Frame should be forwarded unchanged when a surface is already attached.");

        bitmap.Dispose();
        engine.Dispose();
    }

    [TestMethod]
    public async Task ProcessAsync_WhenCancelled_ThrowsImmediately()
    {
        using var surfacePool = new SkiaSurfacePool();
        var orchestrator = new FramePreprocessingOrchestrator(surfacePool, NullLogger<FramePreprocessingOrchestrator>.Instance);

        using var immutableSource = SkiaTestImageFactory.CreateLinearGradientImage(TestWidth, TestHeight);
        var bitmap = SkiaImageUtilities.CreateBitmapCopy(immutableSource, SKColorType.RgbaF16, SKAlphaType.Premul);

        var (frame, engine) = CreateAdapterFrame(bitmap, immutableSource, pixelLease: null, surface: null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => orchestrator.ProcessAsync(frame, cts.Token));

        bitmap.Dispose();
        engine.Dispose();
    }

    private static async Task AssertLinear8BitColorRoundTripAsync(SKColorType colorType)
    {
        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        const float redScale = 0.72f;
        const float greenScale = 0.58f;
        const float blueValue = 0.27f;

        using var surfacePool = new SkiaSurfacePool();
        var orchestrator = new FramePreprocessingOrchestrator(surfacePool, NullLogger<FramePreprocessingOrchestrator>.Instance);

        var sourceBitmap = SkiaTestImageFactory.CreateLinearGradientBitmap(TestWidth, TestHeight, colorType, SKAlphaType.Premul, colorSpace, redScale, greenScale, blueValue);
        var expectedBytes = SkiaTestImageFactory.GetBitmapPixelBuffer(sourceBitmap);
        var immutableSource = SkiaTestImageFactory.CreateLinearGradientImage(TestWidth, TestHeight, colorType, SKAlphaType.Premul, colorSpace, redScale, greenScale, blueValue);

        var (frame, engine) = CreateAdapterFrame(sourceBitmap, immutableSource, pixelLease: null, surface: null);

        var result = await orchestrator.ProcessAsync(frame, CancellationToken.None);
        Assert.IsTrue(result.IsSuccessful, $"Preprocessing should succeed for linear {colorType} inputs.");

        var processed = result.Value;
        Assert.IsNotNull(processed.ImmutableImage, "Immutable image should be materialized after preprocessing.");
    Assert.IsNotNull(processed.PixelLease, "Processed frame should expose a refreshed pixel lease.");

        var processedBytes = SkiaTestImageFactory.GetBytePixelBuffer(processed.ImmutableImage!, colorType);
        AssertBuffersWithinTolerance(expectedBytes, processedBytes, tolerance: 2, context: $"linear {colorType}");

        processed.PixelLease?.Dispose();
        processed.ImmutableImage?.Dispose();
        processed.Bitmap.Dispose();
        engine.Dispose();
    }

    private static void AssertBuffersWithinTolerance(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, int tolerance, string context)
    {
        Assert.AreEqual(expected.Length, actual.Length, $"{context}: Buffer lengths must match.");
        for (var i = 0; i < expected.Length; i++)
        {
            var difference = Math.Abs(expected[i] - actual[i]);
            if (difference > tolerance)
            {
                Assert.Fail($"{context}: Difference {difference} at index {i} exceeds tolerance {tolerance}. Expected {expected[i]}, actual {actual[i]}.");
            }
        }
    }

    private static (CameraAdapterBase.AdapterFrame Frame, StarFieldEngine Engine) CreateAdapterFrame(SKBitmap bitmap, SKImage? immutable, SkiaPixelLease? pixelLease, SKSurface? surface)
    {
        var rig = RigPresets.MockAsi174_Fujinon;
        var timestamp = DateTimeOffset.UtcNow;
        var engine = new StarFieldEngine(rig, TestLatitude, TestLongitude, timestamp.UtcDateTime, flipHorizontal: false, applyRefraction: true, horizonPadding: 0.95);

        var frame = new CameraAdapterBase.AdapterFrame(
            Bitmap: bitmap,
            PixelLease: pixelLease,
            ImmutableImage: immutable,
            Surface: surface,
            Engine: engine,
            Timestamp: timestamp,
            LatitudeDeg: TestLatitude,
            LongitudeDeg: TestLongitude,
            FlipHorizontal: false,
            HorizonPadding: 0.95,
            ApplyRefraction: true,
            Exposure: new ExposureSettings(500, 200, false, false));

        return (frame, engine);
    }
}
