using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class EndToEndPipelineBenchmarks
{
    private readonly RollingFrameStacker _stacker = new();
    private FrameFilterPipeline _pipeline = default!;
    private CameraConfiguration _configuration = default!;

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

        _pipeline = new FrameFilterPipeline(filters, NullLogger<FrameFilterPipeline>.Instance);

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
                return processed.ImageBytes.Length;
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
}
