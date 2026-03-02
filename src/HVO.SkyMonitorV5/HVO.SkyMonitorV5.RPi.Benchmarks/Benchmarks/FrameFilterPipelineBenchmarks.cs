using System;
using HVO.Core.Results;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
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
public class FrameFilterPipelineBenchmarks
{
    private FrameFilterPipeline _pipeline = default!;
    private FrameComposer _frameComposer = default!;
    private CameraConfiguration _configuration = default!;
    private SyntheticFilter[] _filters = Array.Empty<SyntheticFilter>();
    private SkiaSurfacePool _surfacePool = default!;
    private ProcessedFrameEncoder _encoder = default!;

    [Params(1, 3, 5)]
    public int FilterCount { get; set; }

    [Params(512, 1024)]
    public int FrameWidth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _filters = Enumerable.Range(0, FilterCount)
            .Select(index => new SyntheticFilter($"Synthetic_{index}", strokes: 16 + index * 8))
            .ToArray();

        _surfacePool = new SkiaSurfacePool();
        _frameComposer = new FrameComposer(_surfacePool, NullLogger<FrameComposer>.Instance);
        _pipeline = new FrameFilterPipeline(_filters, _frameComposer, NullLogger<FrameFilterPipeline>.Instance);

        var rigAdapter = new DummyRigAdapter(RigPresets.MockAsi174_Fujinon);
        var fitsEncoder = new NoopFitsFrameEncoder();

        _encoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            fitsEncoder,
            rigAdapter);

        _configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 4,
            EnableImageOverlays: true,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 4,
            StackingBufferIntegrationSeconds: 10,
            FrameFilters: _filters.Select(filter => filter.Name).ToArray(),
            ProcessedImageEncoding: new ImageEncodingSettings());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _surfacePool.Dispose();
    }

    [Benchmark(Description = "Process stacked frame with synthetic filters")]
    public async Task<int> ProcessFrameAsync()
    {
        var stackResult = BenchmarkDataFactory.CreateStackResult(FrameWidth, FrameWidth);
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

    private sealed class SyntheticFilter : IFrameFilter
    {
        private readonly int _strokes;

        public SyntheticFilter(string name, int strokes)
        {
            Name = name;
            _strokes = strokes;
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
                Color = SKColors.White.WithAlpha(32),
                StrokeWidth = 2,
                BlendMode = SKBlendMode.Plus
            };

            var center = new SKPoint(bitmap.Width / 2f, bitmap.Height / 2f);
            for (var i = 0; i < _strokes; i++)
            {
                var angle = (float)(i * Math.PI * 2 / _strokes);
                var radius = MathF.Sqrt(i + 1) * 32f;
                var offset = new SKPoint(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                canvas.DrawLine(center - offset, center + offset, paint);
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

    // No-op FITS encoder placeholder
    private sealed class NoopFitsFrameEncoder : IFitsFrameEncoder
    {
        public ProcessedFrameDelivery EncodeRaw(SKBitmap bitmap, CapturedImage capture, RigSpec rig, FitsEncodingOptions? options)
            => new(Array.Empty<byte>(), "application/fits", "fits");

        public ProcessedFrameDelivery EncodeProcessed(ProcessedFrame frame, RigSpec rig, FitsEncodingOptions? options)
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
