using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Composition;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Skia;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class EndToEndPipelineBenchmarks
{
    private RollingFrameStacker _stacker = default!;
    private FrameComposer _frameComposer = default!;
    private FrameFilterPipeline _pipeline = default!;
    private CameraConfiguration _configuration = default!;
    private SkiaSurfacePool _surfacePool = default!;
    private ProcessedFrameEncoder _encoder = default!;

    [Params(1, 4)]
    public int StackingFrameCount { get; set; }

    [Params(1024)]
    public int FrameWidth { get; set; }

    [Params(2, 4)]
    public int SyntheticFilterCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var filters = new IFrameFilter[SyntheticFilterCount];
        for (var i = 0; i < SyntheticFilterCount; i++)
        {
            filters[i] = new SyntheticOverlayFilter($"SyntheticOverlay_{i}", 12 + i * 4);
        }

        _surfacePool = new SkiaSurfacePool();
        _frameComposer = new FrameComposer(_surfacePool, NullLogger<FrameComposer>.Instance);
        _stacker = new RollingFrameStacker(_surfacePool, NullLogger<RollingFrameStacker>.Instance);
        _pipeline = new FrameFilterPipeline(filters, _frameComposer, NullLogger<FrameFilterPipeline>.Instance);

        // Set up minimal dependencies for the updated encoder signature
        var fitsOptions = new StaticOptionsMonitor<FitsExportOptions>(new FitsExportOptions
        {
            EnableForProcessed = false,
            EnableForRaw = false
        });
        var rigAdapter = new DummyRigAdapter(RigPresets.MockAsi174_Fujinon);
        var fitsEncoder = new NoopFitsFrameEncoder();

        _encoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            fitsEncoder,
            rigAdapter,
            fitsOptions);

        var bufferMinimum = Math.Max(24, StackingFrameCount);

        _configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: StackingFrameCount,
            EnableImageOverlays: true,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: bufferMinimum,
            StackingBufferIntegrationSeconds: 120,
            FrameFilters: filters.Select(filter => filter.Name).ToArray(),
            ProcessedImageEncoding: new ImageEncodingSettings());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _surfacePool.Dispose();
    }

    [Benchmark(Description = "Capture + stack + filter pipeline")]
    public async Task<int> CaptureStackProcessAsync()
    {
        var capture = BenchmarkDataFactory.CreateCapturedImage(FrameWidth, FrameWidth);
        try
        {
            var stackResult = _stacker.Accumulate(capture, _configuration);
            try
            {
                var processed = await _pipeline.ProcessAsync(stackResult, _configuration, CancellationToken.None).ConfigureAwait(false);
                var delivery = _encoder.Encode(processed);
                processed.ImmutableImage.Dispose();
                return delivery.Payload.Length;
            }
            finally
            {
                BenchmarkDataFactory.DisposeFrameResult(stackResult);
            }
        }
        finally
        {
            BenchmarkDataFactory.DisposeCapturedImage(capture);
        }
    }

    private sealed class SyntheticOverlayFilter : IFrameFilter
    {
        private readonly int _rings;

        public SyntheticOverlayFilter(string name, int rings)
        {
            Name = name;
            _rings = rings;
        }

        public string Name { get; }

        public bool ShouldApply(CameraConfiguration configuration) => true;

        public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
            => ApplyAsync(bitmap, stackResult, configuration, renderContext: null, cancellationToken);

        public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, FrameRenderContext? renderContext, CancellationToken cancellationToken)
        {
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.DeepSkyBlue.WithAlpha(64),
                StrokeWidth = 3,
                Style = SKPaintStyle.Stroke
            };

            var center = new SKPoint(bitmap.Width / 2f, bitmap.Height / 2f);
            var maxRadius = Math.Min(bitmap.Width, bitmap.Height) / 2f;
            for (var i = 1; i <= _rings; i++)
            {
                var radius = maxRadius * (i / (float)_rings);
                canvas.DrawCircle(center, radius, paint);
            }

            return ValueTask.CompletedTask;
        }
    }

    // Minimal, static options monitor for benchmarks
    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T> where T : class, new()
    {
        private readonly T _value;
        public StaticOptionsMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable OnChange(Action<T, string?> listener) => NoopDisposable.Instance;

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }
    }

    // No-op FITS encoder placeholder; not used when FITS export disabled
    private sealed class NoopFitsFrameEncoder : IFitsFrameEncoder
    {
        public ProcessedFrameDelivery EncodeRaw(SKImage image, RawFrameSnapshot frame, RigSpec rig, FitsExportOptions options)
            => new(Array.Empty<byte>(), "application/fits", "fits");

        public ProcessedFrameDelivery EncodeProcessed(ProcessedFrame frame, RigSpec rig, FitsExportOptions options)
            => new(Array.Empty<byte>(), "application/fits", "fits");
    }

    // Lightweight rig adapter for benchmarks
    private sealed class DummyRigAdapter : IRigAcquisitionAdapter
    {
        public DummyRigAdapter(RigSpec rig) => ActiveRig = rig;
        public RigSpec ActiveRig { get; }
        public bool IsRunning => false;
        public RigAdapterLifecycleState CurrentState => RigAdapterLifecycleState.Stopped;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<Result<bool>> StartAsync(CancellationToken cancellationToken) => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> PauseAsync(CancellationToken cancellationToken) => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> ResumeAsync(CancellationToken cancellationToken) => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> StopAsync(CancellationToken cancellationToken) => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> ReloadAsync(RigSpec rig, CancellationToken cancellationToken, bool forceReload = false) => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<CapturedImage>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken) => Task.FromResult(Result<CapturedImage>.Failure(new InvalidOperationException("Not supported in benchmark")));
    }
}
