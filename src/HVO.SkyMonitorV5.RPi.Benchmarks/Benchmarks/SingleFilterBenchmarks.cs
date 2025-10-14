using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class SingleFilterBenchmarks
{
    private FrameStackResult _stackResult = default!;
    private CameraConfiguration _configuration = default!;
    private IFrameFilter _filter = default!;

    [Params(512, 1024)]
    public int FrameWidth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _stackResult = BenchmarkDataFactory.CreateStackResult(FrameWidth, FrameWidth);

        _filter = new SyntheticOverlayFilter("Synthetic_Single", ringCount: 16);

        _configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 1,
            EnableImageOverlays: true,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 1,
            StackingBufferIntegrationSeconds: 5,
            FrameFilters: new[] { _filter.Name },
            ProcessedImageEncoding: new ImageEncodingSettings());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkDataFactory.DisposeFrameResult(_stackResult);
    }

    [Benchmark(Description = "Apply synthetic overlay filter")]
    public async Task<int> ApplyFilterAsync()
    {
        using var bitmap = _stackResult.StackedImage.Copy() ?? throw new InvalidOperationException("Failed to clone stacked bitmap");
        await _filter.ApplyAsync(bitmap, _stackResult, _configuration, renderContext: null, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        return bitmap.Width * bitmap.Height;
    }

    private sealed class SyntheticOverlayFilter : IFrameFilter
    {
        private readonly int _ringCount;

        public SyntheticOverlayFilter(string name, int ringCount)
        {
            Name = name;
            _ringCount = ringCount;
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
                Color = SKColors.CornflowerBlue.WithAlpha(72),
                StrokeWidth = 2.5f,
                Style = SKPaintStyle.Stroke
            };

            var center = new SKPoint(bitmap.Width / 2f, bitmap.Height / 2f);
            var maxRadius = Math.Min(bitmap.Width, bitmap.Height) * 0.48f;

            for (var ring = 1; ring <= _ringCount; ring++)
            {
                var radius = maxRadius * (ring / (float)_ringCount);
                canvas.DrawCircle(center, radius, paint);
            }

            return ValueTask.CompletedTask;
        }
    }
}
