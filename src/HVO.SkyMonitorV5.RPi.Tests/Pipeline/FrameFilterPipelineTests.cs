using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
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
        var pipeline = new FrameFilterPipeline(new IFrameFilter[] { filter }, NullLogger<FrameFilterPipeline>.Instance);

        var processed = await pipeline.ProcessAsync(stackResult.Result, configuration, CancellationToken.None);

        Assert.AreEqual(1, processed.AppliedFilters.Count, "Pipeline should record applied filters.");
        Assert.AreEqual("TestFilter", processed.AppliedFilters[0]);

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
    }

    [TestMethod]
    public async Task ProcessAsync_AccumulatesTelemetryAcrossInvocations()
    {
        var configuration = CreateConfiguration("PerfFilter");
        var filter = new CapturingTestFilter("PerfFilter", async cancellationToken =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
        });
        var pipeline = new FrameFilterPipeline(new IFrameFilter[] { filter }, NullLogger<FrameFilterPipeline>.Instance);

        using (var stack1 = CreateStackResult())
        {
            await pipeline.ProcessAsync(stack1.Result, configuration, CancellationToken.None);
        }

        using (var stack2 = CreateStackResult())
        {
            await pipeline.ProcessAsync(stack2.Result, configuration, CancellationToken.None);
        }

        var metrics = pipeline.GetMetricsSnapshot();
        Assert.AreEqual(1, metrics.Filters.Count, "Telemetry should aggregate per-filter.");
        var entry = metrics.Filters[0];
        Assert.AreEqual(2, entry.AppliedCount, "Running the pipeline twice should increment applied count.");
        Assert.IsTrue(entry.LastDurationMilliseconds is >= 0, "Last duration should be populated.");
    Assert.IsTrue(entry.AverageDurationMilliseconds is >= 0, "Average duration should be calculated.");
    }

    private static CameraConfiguration CreateConfiguration(string filterName)
        => new(
            EnableStacking: true,
            StackingFrameCount: 1,
            EnableImageOverlays: true,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 1,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: new[] { filterName },
            ProcessedImageEncoding: new ImageEncodingSettings());

    private const double TestLatitude = 35.1987;
    private const double TestLongitude = -114.0539;
    private const string TestRigName = "MockASI174MM + Fujinon 2.7mm";

    private static StackResultHarness CreateStackResult()
    {
        var rig = RigPresets.MockAsi174_Fujinon;
        var timestamp = DateTimeOffset.UtcNow;
        var engine = new StarFieldEngine(rig, TestLatitude, TestLongitude, timestamp.UtcDateTime, flipHorizontal: true, applyRefraction: true, horizonPadding: 0.95);

        var disposed = false;
        var frameContext = new FrameContext(
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
        var stackResult = new FrameStackResult(stacked, original, timestamp, exposure, frameContext, FramesStacked: 1, IntegrationMilliseconds: exposure.ExposureMilliseconds);

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
            if (!ReferenceEquals(_stacked, _original))
            {
                Result.OriginalImage.Dispose();
            }
        }
    }
}
