using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class FrameFilterPipelineBenchmarks
{
    private FrameFilterPipeline _pipeline = default!;
    private CameraConfiguration _configuration = default!;
    private SyntheticFilter[] _filters = Array.Empty<SyntheticFilter>();

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

        _pipeline = new FrameFilterPipeline(_filters, NullLogger<FrameFilterPipeline>.Instance);

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

    [Benchmark(Description = "Process stacked frame with synthetic filters")]
    public async Task<int> ProcessFrameAsync()
    {
        var stackResult = BenchmarkDataFactory.CreateStackResult(FrameWidth, FrameWidth);
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
}
