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
public class OverlayCompositionBenchmarks
{
    private FrameStackResult _stackResult = default!;
    private CameraConfiguration _configuration = default!;
    private IFrameFilter[] _filters = Array.Empty<IFrameFilter>();

    [Params(1, 3, 5)]
    public int OverlayCount { get; set; }

    [Params(1024)]
    public int FrameWidth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _stackResult = BenchmarkDataFactory.CreateStackResult(FrameWidth, FrameWidth);

        _filters = new IFrameFilter[OverlayCount];
        for (var i = 0; i < OverlayCount; i++)
        {
            _filters[i] = new SyntheticOverlayFilter($"Overlay_{i}", armCount: 12 + i * 2);
        }

        var filterNames = new string[_filters.Length];
        for (var i = 0; i < _filters.Length; i++)
        {
            filterNames[i] = _filters[i].Name;
        }

        _configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 1,
            EnableImageOverlays: true,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 1,
            StackingBufferIntegrationSeconds: 5,
            FrameFilters: filterNames,
            ProcessedImageEncoding: new ImageEncodingSettings());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkDataFactory.DisposeFrameResult(_stackResult);
    }

    [Benchmark(Description = "Compose overlays onto stacked frame")]
    public async Task<int> ComposeAsync()
    {
        using var working = _stackResult.StackedImage.Copy() ?? throw new InvalidOperationException("Failed to clone stacked bitmap");

        foreach (var filter in _filters)
        {
            await filter.ApplyAsync(working, _stackResult, _configuration, renderContext: null, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }

        using var image = SKImage.FromBitmap(working);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.Size;
    }

    private sealed class SyntheticOverlayFilter : IFrameFilter
    {
        private readonly int _armCount;

        public SyntheticOverlayFilter(string name, int armCount)
        {
            Name = name;
            _armCount = armCount;
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
                Color = SKColors.LightGreen.WithAlpha(96),
                StrokeWidth = 2,
                Style = SKPaintStyle.Stroke
            };

            var center = new SKPoint(bitmap.Width / 2f, bitmap.Height / 2f);
            var maxRadius = Math.Min(bitmap.Width, bitmap.Height) * 0.45f;

            for (var arm = 0; arm < _armCount; arm++)
            {
                var angle = (float)(arm * (Math.PI * 2 / _armCount));
                var armRadius = maxRadius;
                var end = new SKPoint(center.X + MathF.Cos(angle) * armRadius, center.Y + MathF.Sin(angle) * armRadius);
                canvas.DrawLine(center, end, paint);
            }

            using var maskPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = SKColors.White.WithAlpha(48),
                StrokeWidth = 3
            };
            canvas.DrawCircle(center, maxRadius, maskPaint);

            return ValueTask.CompletedTask;
        }
    }
}
