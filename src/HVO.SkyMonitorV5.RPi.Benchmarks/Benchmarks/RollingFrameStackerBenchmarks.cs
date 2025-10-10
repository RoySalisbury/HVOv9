using BenchmarkDotNet.Attributes;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class RollingFrameStackerBenchmarks
{
    private readonly RollingFrameStacker _stacker = new();
    private CameraConfiguration _configuration = default!;

    [Params(1, 4, 8)]
    public int StackingFrameCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
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
