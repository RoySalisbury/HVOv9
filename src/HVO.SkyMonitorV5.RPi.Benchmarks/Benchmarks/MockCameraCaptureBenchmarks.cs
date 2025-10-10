using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Benchmarks.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class MockCameraCaptureBenchmarks
{
    private MockCameraAdapter _adapter = default!;
    private ExposureSettings _exposure = default!;

    [GlobalSetup]
    public async Task Setup()
    {
        var locationOptions = new ObservatoryLocationOptions
        {
            LatitudeDegrees = 35.1987,
            LongitudeDegrees = -114.0539
        };

        var starCatalogOptions = new StarCatalogOptions
        {
            MagnitudeLimit = 6.0,
            TopStarCount = 200,
            StratifiedSelection = false
        };

        var cardinalOptions = new CardinalDirectionsOptions
        {
            SwapEastWest = false
        };

        var starRepository = new BenchmarkStarRepository();
        var scopeFactory = new BenchmarkServiceScopeFactory(starRepository);

        _adapter = new MockCameraAdapter(
            new StaticOptionsMonitor<ObservatoryLocationOptions>(locationOptions),
            new StaticOptionsMonitor<StarCatalogOptions>(starCatalogOptions),
            new StaticOptionsMonitor<CardinalDirectionsOptions>(cardinalOptions),
            scopeFactory,
            RigPresets.MockAsi174_Fujinon,
            NullLogger<MockCameraAdapter>.Instance);

        _exposure = new ExposureSettings(ExposureMilliseconds: 1_500, Gain: 220, AutoExposure: false, AutoGain: false);

        var initResult = await _adapter.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        if (initResult.IsFailure)
        {
            throw initResult.Error ?? new InvalidOperationException("Failed to initialize mock camera adapter.");
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_adapter is not null)
        {
            await _adapter.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            await _adapter.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Benchmark(Description = "Mock camera capture to bitmap + context")]
    public async Task<int> CaptureAsync()
    {
        var result = await _adapter.CaptureAsync(_exposure, CancellationToken.None).ConfigureAwait(false);
        if (result.IsFailure)
        {
            throw result.Error ?? new InvalidOperationException("Capture failed.");
        }

        try
        {
            using var context = result.Value.Context;
            using var bitmap = result.Value.Image;
            return bitmap.Width * bitmap.Height;
        }
        finally
        {
            // Context and bitmap disposed via using statements above.
        }
    }
}
