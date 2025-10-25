using BenchmarkDotNet.Attributes;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Skia;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class RollingFrameStackerBenchmarks
{
    private RollingFrameStacker _stacker = default!;
    private SkiaSurfacePool _surfacePool = default!;
    private CameraConfiguration _configuration = default!;

    [Params(1, 4, 8)]
    public int StackingFrameCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _surfacePool = new SkiaSurfacePool();
        _stacker = new RollingFrameStacker(_surfacePool, NullLogger<RollingFrameStacker>.Instance);

        _configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: StackingFrameCount,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: StackingFrameCount,
            StackingBufferIntegrationSeconds: StackingFrameCount * 5,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _surfacePool.Dispose();
    }

    [Benchmark(Description = "Accumulate single frame into rolling buffer")]
    public int AccumulateFrame()
    {
        var capture = BenchmarkDataFactory.CreateCapturedImage();
        try
        {
            var result = _stacker.Accumulate(capture, _configuration);
            try
            {
                return result.FramesStacked;
            }
            finally
            {
                BenchmarkDataFactory.DisposeFrameResult(result);
            }
        }
        finally
        {
            BenchmarkDataFactory.DisposeCapturedImage(capture);
        }
    }
}
