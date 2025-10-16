# SkyMonitor v5 Performance Benchmarking

This guide captures the current options for measuring and profiling the all-sky capture pipeline.

## Runtime diagnostics

The capture loop now records high-frequency timings at the `Debug` log level:

- Camera capture latency
- Frame stacker accumulation time
- Filter pipeline processing time
- End-to-end frame latency (capture → publish)

Enable `Debug` logging for `HVO.SkyMonitorV5.RPi.HostedServices.AllSkyCaptureService` and `HVO.SkyMonitorV5.RPi.Pipeline.FrameFilterPipeline` to surface the new metrics:

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "HVO.SkyMonitorV5.RPi.HostedServices.AllSkyCaptureService": "Debug",
    "HVO.SkyMonitorV5.RPi.Pipeline.FrameFilterPipeline": "Debug"
  }
}
```

The capture service emits per-frame summaries similar to:

```
Captured frame at 2025-10-08T03:14:15Z (capture 45.6ms, stack 8.2ms, filters 410.3ms, total 612.7ms). Next capture in 387ms.
```

The filter pipeline logs a breakdown of the copy, per-filter execution, and PNG encoding time:

```
Filter pipeline completed in 410.3ms (copy 5.4ms, encode 180.7ms). Filters: CelestialAnnotations:320.1ms, ConstellationFigures:45.9ms.
```

These diagnostics run in-process and translate directly to Raspberry Pi deployments when the same log levels are enabled.

> Tip: Adjust `CameraPipeline.ProcessedImageEncoding` in `appsettings.json` (default `"Format": "Jpeg", "Quality": 90`) to compare encoding formats without touching code. The pipeline will emit the correct `Content-Type` automatically in API responses.

## BenchmarkDotNet starter harness

For repeatable micro-benchmarks, add a dedicated project with [BenchmarkDotNet](https://benchmarkdotnet.org/):

1. Create a new console project (for example `src/HVO.SkyMonitorV5.RPi.Benchmarks`).
2. Reference `BenchmarkDotNet` and the pipeline project.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\HVO.SkyMonitorV5.RPi\HVO.SkyMonitorV5.RPi.csproj" />
  </ItemGroup>
</Project>
```

3. Seed a benchmark that creates a realistic `FrameStackResult` (for example a 1936×1216 `SKBitmap`) and measures the `FrameFilterPipeline`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Composition;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using HVO.SkyMonitorV5.RPi.Skia;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

BenchmarkRunner.Run<PipelineBenchmarks>();

public class PipelineBenchmarks : IDisposable
{
  private readonly SkiaSurfacePool _surfacePool = new();
  private readonly FrameComposer _composer;
  private readonly FrameFilterPipeline _pipeline;
  private readonly CameraConfiguration _configuration;
  private readonly FrameStackResult _frame;

  public PipelineBenchmarks()
  {
    _composer = new FrameComposer(_surfacePool, NullLogger<FrameComposer>.Instance);
    _pipeline = new FrameFilterPipeline(Array.Empty<IFrameFilter>(), _composer, NullLogger<FrameFilterPipeline>.Instance);

    _configuration = new CameraConfiguration(
      EnableStacking: true,
      StackingFrameCount: 1,
      EnableImageOverlays: false,
      EnableCircularApertureMask: false,
      StackingBufferMinimumFrames: 1,
      StackingBufferIntegrationSeconds: 0,
      FrameFilters: Array.Empty<string>(),
      ProcessedImageEncoding: new ImageEncodingSettings());

    var bitmap = new SKBitmap(1936, 1216);
    var exposure = new ExposureSettings(1000, 200, autoExposure: false, autoGain: false);

    var immutable = SKImage.FromBitmap(bitmap);
    _frame = new FrameStackResult(Guid.NewGuid(), bitmap, bitmap, DateTimeOffset.UtcNow, exposure, Context: null, FramesStacked: 1, IntegrationMilliseconds: exposure.ExposureMilliseconds)
    {
      StackedImmutableImage = immutable,
      OriginalImmutableImage = immutable
    };
  }

  [Benchmark]
  public Task ProcessBaselineAsync()
    => _pipeline.ProcessAsync(_frame, _configuration, CancellationToken.None);

  public void Dispose()
  {
    _frame.StackedImage.Dispose();
    if (!ReferenceEquals(_frame.StackedImage, _frame.OriginalImage))
    {
      _frame.OriginalImage.Dispose();
    }

    _frame.StackedImmutableImage?.Dispose();
    if (!ReferenceEquals(_frame.StackedImmutableImage, _frame.OriginalImmutableImage))
    {
      _frame.OriginalImmutableImage?.Dispose();
    }

    _surfacePool.Dispose();
  }
}
```
Running `dotnet run -c Release --project src/HVO.SkyMonitorV5.RPi.Benchmarks -- --filter *FrameFilterPipeline*` will produce benchmark tables and highlight hot paths. Expand the harness by wiring the real filters through DI when you want full-fidelity measurements.

## Phase 5 profiling snapshot (2025-10-16)

- Benchmarks collected via `dotnet run -c Release -- --filter *FrameFilterPipeline* --artifacts benchmarks/rpi-20251016` after centralizing composition on `FrameComposer`.
- 1024x1024 synthetic frame with three overlays averages **33.98 ms** (stddev 0.33 ms); five overlays pushes to **45.59 ms** (stddev 1.26 ms).
- 512x512 workloads stay within **13.33 ms** even with five overlays, confirming deterministic filters avoid extra surface allocations.
- Full CSV/HTML summaries live in `benchmarks/rpi-20251016/results/`, with the raw runner log stored at `benchmarks/rpi-20251016/HVO.SkyMonitorV5.RPi.Benchmarks.Benchmarks.FrameFilterPipelineBenchmarks-20251016-160710.log`.

## Phase 7 rolling stacker snapshot (2025-10-16)

- Command: `DOTNET_ENVIRONMENT=Benchmark HVO_BENCH_WARMUP_COUNT=1 HVO_BENCH_ITERATION_COUNT=2 HVO_BENCH_LAUNCH_COUNT=1 dotnet run --project src/HVO.SkyMonitorV5.RPi.Benchmarks/HVO.SkyMonitorV5.RPi.Benchmarks.csproj -c Release -- --filter "*Stacker*"` (Processor encoder path enabled).
- Mean accumulation latency (1936×1216 frames): 1 frame → **23.43 ms**, 4 frames → **30.51 ms**, 8 frames → **42.78 ms**. Standard deviation stayed ≤ 4.15 ms across the series despite the new delivery payload encoding.
- Managed allocations remained stable (≈15.8 MB for single-frame flow, ≈31.6 MB when buffering 4–8 frames) indicating the encoder integration did not regress pool usage.
- Artifacts copied to `benchmarks/rpi-20251016/rolling-frame-stacker/` (CSV, HTML, GitHub markdown, and runner log `HVO.SkyMonitorV5.RPi.Benchmarks.Benchmarks.RollingFrameStackerBenchmarks-20251016-220132.log`).

## Next steps

- Profile on-device with `dotnet-counters` or `dotnet-trace` to observe CPU/concurrency behaviour under load.
- Use the benchmark harness to evaluate changes (for example, alternate image encoders or filter tweaks) before deploying to the observatory hardware.
- Combine runtime diagnostics with BenchmarkDotNet results to zoom in on bottlenecks.
- For container-level soaks, run `scripts/run-skymonitor-benchmark-matrix.sh` with `SCENARIO_FILTER=mono-bg-on` (comma-separated for multiple scenarios) and `RUN_DURATION=120` for a two-minute local baseline. Pair the script with `DOCKER_CONTEXT=hvo-local`, point `DATA_ROOT` at `benchmarks/<host>/datastore`, and export `TAIL_LOGS=false` if you invoke `deploy-skymonitor-rpi.sh` first from within VS Code terminals to avoid long-running log streams.
