using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Composition;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using HVO.SkyMonitorV5.RPi.Skia;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Pipeline;

[TestClass]
public sealed class FrameFilterPipelineTests
{
    [TestMethod]
    public async Task ProcessAsync_PassesRenderContextAndDisposesFrameContext()
    {
        var configuration = CreateConfiguration("TestFilter");
        using var stackResult = CreateStackResult();

        var filter = new CapturingTestFilter("TestFilter");
    using var surfacePool = new SkiaSurfacePool();
    var composer = new FrameComposer(surfacePool, NullLogger<FrameComposer>.Instance);
    var pipeline = new FrameFilterPipeline(new IFrameFilter[] { filter }, composer, NullLogger<FrameFilterPipeline>.Instance);

        var processed = await pipeline.ProcessAsync(stackResult.Result, configuration, CancellationToken.None);

        Assert.AreEqual(1, processed.AppliedFilters.Count, "Pipeline should record applied filters.");
        Assert.AreEqual("TestFilter", processed.AppliedFilters[0]);
    Assert.AreEqual(1, processed.FilterExecutions.Count, "Pipeline should capture filter execution metadata.");
    Assert.IsTrue(processed.SurfaceMilliseconds >= 0, "Surface preparation timing should be captured.");

        Assert.IsNotNull(filter.LastContext, "Filter should receive a render context instance.");
        Assert.AreEqual(TestLatitude, filter.LastContext!.LatitudeDeg, 1e-6, "Latitude should flow through render context.");
        Assert.AreEqual(TestLongitude, filter.LastContext!.LongitudeDeg, 1e-6, "Longitude should flow through render context.");
        Assert.AreEqual(TestRigName, filter.LastContext!.Rig.Name, "Rig metadata should be preserved.");

        Assert.IsTrue(stackResult.WasDisposed(), "FrameContext should be disposed after processing.");

        var metrics = pipeline.GetMetricsSnapshot();
        Assert.AreEqual(1, metrics.Filters.Count, "Telemetry should contain one filter entry.");
        var entry = metrics.Filters[0];
        Assert.AreEqual("TestFilter", entry.FilterName);
        Assert.AreEqual(1, entry.AppliedCount);
        Assert.IsTrue(entry.LastDurationMilliseconds >= 0);

        processed.ImmutableImage?.Dispose();
    }

    [TestMethod]
    public async Task ProcessAsync_AccumulatesTelemetryAcrossInvocations()
    {
        var configuration = CreateConfiguration("PerfFilter");
        var filter = new CapturingTestFilter("PerfFilter", async cancellationToken =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
        });
    using var surfacePool = new SkiaSurfacePool();
    var composer = new FrameComposer(surfacePool, NullLogger<FrameComposer>.Instance);
    var pipeline = new FrameFilterPipeline(new IFrameFilter[] { filter }, composer, NullLogger<FrameFilterPipeline>.Instance);

        using (var stack1 = CreateStackResult())
        {
            var processed = await pipeline.ProcessAsync(stack1.Result, configuration, CancellationToken.None);
            Assert.AreEqual(1, processed.FilterExecutions.Count, "Filter execution timings should be recorded per invocation.");
            processed.ImmutableImage?.Dispose();
        }

        using (var stack2 = CreateStackResult())
        {
            var processed = await pipeline.ProcessAsync(stack2.Result, configuration, CancellationToken.None);
            Assert.AreEqual(1, processed.FilterExecutions.Count, "Filter execution timings should be recorded per invocation.");
            processed.ImmutableImage?.Dispose();
        }

        var metrics = pipeline.GetMetricsSnapshot();
        Assert.AreEqual(1, metrics.Filters.Count, "Telemetry should aggregate per-filter.");
        var entry = metrics.Filters[0];
        Assert.AreEqual(2, entry.AppliedCount, "Running the pipeline twice should increment applied count.");
        Assert.IsTrue(entry.LastDurationMilliseconds is >= 0, "Last duration should be populated.");
        Assert.IsTrue(entry.AverageDurationMilliseconds is >= 0, "Average duration should be calculated.");
    }

    [TestMethod]
    public async Task ProcessAsync_InvokesImageFrameFilter()
    {
        var configuration = CreateConfiguration("SurfaceFilter");
        var filter = new SurfaceFillFilter();
    using var surfacePool = new SkiaSurfacePool();
    var composer = new FrameComposer(surfacePool, NullLogger<FrameComposer>.Instance);
    var pipeline = new FrameFilterPipeline(new IFrameFilter[] { filter }, composer, NullLogger<FrameFilterPipeline>.Instance);

        using var stack = CreateStackResult();
        var processed = await pipeline.ProcessAsync(stack.Result, configuration, CancellationToken.None);

        Assert.AreEqual(1, processed.AppliedFilters.Count, "Surface filter should be recorded as applied.");
        Assert.AreEqual("SurfaceFilter", processed.AppliedFilters[0]);
        Assert.AreEqual(1, filter.InvocationCount, "Image-based filter should be invoked exactly once.");
    Assert.AreEqual(1, processed.FilterExecutions.Count, "Surface filter execution timing should be captured.");

        Assert.IsNotNull(processed.ImmutableImage, "Pipeline should materialize an immutable output image.");
        var snapshot = processed.ImmutableImage!;
        var info = new SKImageInfo(snapshot.Width, snapshot.Height, SKColorType.Rgba8888);
        using var bitmap = new SKBitmap(info);
        Assert.IsTrue(snapshot.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes), "Processed immutable image should be readable.");

        var pixel = bitmap.GetPixel(0, 0);
    Assert.IsTrue(pixel.Red >= 200, "Surface filter should produce a strong red component.");
    Assert.IsTrue(pixel.Green <= 10, "Surface filter should suppress the green channel.");
    Assert.IsTrue(pixel.Blue <= 10, "Surface filter should suppress the blue channel.");

        snapshot.Dispose();
    }

    private static CameraConfiguration CreateConfiguration(params string[] filterNames)
    {
        var filters = filterNames is { Length: > 0 } ? filterNames : Array.Empty<string>();

        return new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 1,
            EnableImageOverlays: true,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 1,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: filters,
            ProcessedImageEncoding: new ImageEncodingSettings());
    }

    private const double TestLatitude = 35.1987;
    private const double TestLongitude = -114.0539;
    private const string TestRigName = "MockASI174MM + Fujinon 2.7mm";

    private static StackResultHarness CreateStackResult()
    {
        var rig = RigPresets.MockAsi174_Fujinon;
        var timestamp = DateTimeOffset.UtcNow;
        var engine = new StarFieldEngine(rig, TestLatitude, TestLongitude, timestamp.UtcDateTime, flipHorizontal: true, applyRefraction: true, horizonPadding: 0.95);
        var frameId = Guid.NewGuid();

        var disposed = false;
        var frameContext = new FrameContext(
            frameId,
            rig,
            engine,
            timestamp,
            TestLatitude,
            TestLongitude,
            FlipHorizontal: true,
            HorizonPadding: 0.95,
            ApplyRefraction: true,
            DisposeAction: _ => disposed = true);

        var exposure = new ExposureSettings(ExposureMilliseconds: 1_000, Gain: 200, AutoExposure: false, AutoGain: false);
        var stacked = new SKBitmap(width: 8, height: 8);
        var original = new SKBitmap(width: 8, height: 8);
        var stackedImage = SKImage.FromBitmap(stacked) ?? throw new InvalidOperationException("Unable to snapshot stacked bitmap for test harness.");
        var originalImage = SKImage.FromBitmap(original) ?? throw new InvalidOperationException("Unable to snapshot original bitmap for test harness.");
        var stackResult = new FrameStackResult(frameId, stacked, original, timestamp, exposure, frameContext, FramesStacked: 1, IntegrationMilliseconds: exposure.ExposureMilliseconds)
        {
            StackedImmutableImage = stackedImage,
            OriginalImmutableImage = originalImage
        };

        return new StackResultHarness(stackResult, () => disposed, stacked, original);
    }

    private sealed class CapturingTestFilter : IFrameFilter
    {
        private readonly string _name;
        private readonly Func<CancellationToken, ValueTask>? _onApplyAsync;

        public CapturingTestFilter(string name, Func<CancellationToken, ValueTask>? onApplyAsync = null)
        {
            _name = name;
            _onApplyAsync = onApplyAsync;
        }

        public string Name => _name;

        public FrameRenderContext? LastContext { get; private set; }

        public int ApplyInvocations { get; private set; }

        public bool ShouldApply(CameraConfiguration configuration) => true;

        public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
            => ApplyAsync(bitmap, stackResult, configuration, renderContext: null, cancellationToken);

        public async ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, FrameRenderContext? renderContext, CancellationToken cancellationToken)
        {
            ApplyInvocations++;
            LastContext = renderContext;

            if (_onApplyAsync is not null)
            {
                await _onApplyAsync.Invoke(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class SurfaceFillFilter : IImageFrameFilter
    {
        public string Name => "SurfaceFilter";

        public int InvocationCount { get; private set; }

        public bool ShouldApply(CameraConfiguration configuration) => true;

        public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public async ValueTask ApplyAsync(FilterFrame frame, FrameStackResult stackResult, CameraConfiguration configuration, FrameRenderContext? renderContext, CancellationToken cancellationToken)
        {
            InvocationCount++;
            cancellationToken.ThrowIfCancellationRequested();

            using var paint = new SKPaint { Color = new SKColor(255, 32, 32, 255), IsAntialias = false };
            frame.Surface.Canvas.DrawRect(new SKRect(0, 0, frame.Surface.Canvas.DeviceClipBounds.Width, frame.Surface.Canvas.DeviceClipBounds.Height), paint);
            frame.Surface.Canvas.Flush();
            await ValueTask.CompletedTask.ConfigureAwait(false);
        }
    }

    private sealed class StackResultHarness : IDisposable
    {
        private readonly SKBitmap _stacked;
        private readonly SKBitmap _original;
        private readonly Func<bool> _wasDisposed;

        public StackResultHarness(FrameStackResult result, Func<bool> wasDisposed, SKBitmap stacked, SKBitmap original)
        {
            Result = result;
            _stacked = stacked;
            _original = original;
            _wasDisposed = wasDisposed;
        }

        public FrameStackResult Result { get; }

        public bool WasDisposed() => _wasDisposed();

        public void Dispose()
        {
            Result.StackedImage.Dispose();
            Result.StackedImmutableImage?.Dispose();
            if (!ReferenceEquals(_stacked, _original))
            {
                Result.OriginalImage.Dispose();
            }
            Result.OriginalImmutableImage?.Dispose();
        }
    }

    [TestMethod]
    public async Task ProcessAsync_ProducesDeterministicOutputAcrossRuns()
    {
        var configuration = CreateConfiguration(
            DeterministicBackgroundFilter.FilterName,
            DeterministicOverlayFilter.FilterName);
        configuration = configuration with
        {
            ProcessedImageEncoding = new ImageEncodingSettings(ImageEncodingFormat.Png, quality: 100)
        };

        using var surfacePool = new SkiaSurfacePool();
        var composer = new FrameComposer(surfacePool, NullLogger<FrameComposer>.Instance);
        var filters = new IFrameFilter[]
        {
            new DeterministicBackgroundFilter(),
            new DeterministicOverlayFilter()
        };

        var pipeline = new FrameFilterPipeline(filters, composer, NullLogger<FrameFilterPipeline>.Instance);

        byte[]? referencePayload = null;
        var expectedFilters = new[]
        {
            DeterministicBackgroundFilter.FilterName,
            DeterministicOverlayFilter.FilterName
        };

        for (var i = 0; i < 3; i++)
        {
            using var stack = CreateStackResult();
            var processed = await pipeline.ProcessAsync(stack.Result, configuration, CancellationToken.None);

            try
            {
                var payload = processed.ImageBytes.ToArray();
                if (referencePayload is null)
                {
                    referencePayload = payload;
                }
                else
                {
                    CollectionAssert.AreEqual(referencePayload, payload, "Pipeline should emit identical payloads for equivalent inputs.");
                }

                CollectionAssert.AreEqual(expectedFilters, processed.AppliedFilters.ToArray(), "Applied filter sequence should remain stable.");
                CollectionAssert.AreEqual(expectedFilters, processed.FilterExecutions.Select(static execution => execution.FilterName).ToArray(), "Filter execution order should mirror filter registrations.");

                foreach (var execution in processed.FilterExecutions)
                {
                    Assert.IsTrue(execution.DurationMilliseconds >= 0, "Filter execution timings must be non-negative.");
                }

                Assert.IsTrue(processed.SurfaceMilliseconds >= 0, "Surface preparation timing must be non-negative.");

                using var data = SKData.CreateCopy(processed.ImageBytes);
                using var encodedImage = SKImage.FromEncodedData(data);
                var info = new SKImageInfo(encodedImage.Width, encodedImage.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var decodedBitmap = new SKBitmap(info);
                Assert.IsTrue(encodedImage.ReadPixels(info, decodedBitmap.GetPixels(), decodedBitmap.RowBytes), "Encoded payload should decode successfully.");

                var centerColor = decodedBitmap.GetPixel(info.Width / 2, info.Height / 2);
                Assert.IsTrue(centerColor.Red >= 180, "Overlay filter should render a strong red accent in the center pixel.");
                Assert.IsTrue(centerColor.Green <= 20, "Overlay filter should keep the center pixel's green channel low.");
                Assert.IsTrue(centerColor.Blue <= 20, "Overlay filter should keep the center pixel's blue channel low.");
            }
            finally
            {
                processed.ImmutableImage?.Dispose();
            }
        }
    }

    private sealed class DeterministicBackgroundFilter : IImageFrameFilter
    {
        public const string FilterName = "DeterministicBackground";
        private static readonly SKColor BackgroundColor = new(32, 64, 196, 255);

        public string Name => FilterName;

        public bool ShouldApply(CameraConfiguration configuration) => true;

        public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ApplyAsync(FilterFrame frame, FrameStackResult stackResult, CameraConfiguration configuration, FrameRenderContext? renderContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var paint = new SKPaint { Color = BackgroundColor, IsAntialias = false, Style = SKPaintStyle.Fill };
            frame.Surface.Canvas.DrawRect(frame.Surface.Canvas.DeviceClipBounds, paint);
            frame.Surface.Canvas.Flush();

            return ValueTask.CompletedTask;
        }
    }

    private sealed class DeterministicOverlayFilter : IImageFrameFilter
    {
        public const string FilterName = "DeterministicOverlay";
        public static readonly SKColor ExpectedCenterColor = new(220, 48, 48, 255);

        public string Name => FilterName;

        public bool ShouldApply(CameraConfiguration configuration) => true;

        public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ApplyAsync(FilterFrame frame, FrameStackResult stackResult, CameraConfiguration configuration, FrameRenderContext? renderContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var canvas = frame.Surface.Canvas;
            var bounds = canvas.DeviceClipBounds;

            using var borderPaint = new SKPaint
            {
                Color = new SKColor(240, 240, 240, 255),
                StrokeWidth = 1f,
                IsAntialias = false,
                Style = SKPaintStyle.Stroke
            };

            canvas.DrawRect(bounds, borderPaint);

            var centerX = (int)bounds.MidX;
            var centerY = (int)bounds.MidY;

            using var centerPaint = new SKPaint
            {
                Color = ExpectedCenterColor,
                IsAntialias = false,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawRect(SKRect.Create(centerX, centerY, 1, 1), centerPaint);
            canvas.Flush();

            return ValueTask.CompletedTask;
        }
    }
}
