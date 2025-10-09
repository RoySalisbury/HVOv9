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
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

BenchmarkRunner.Run<PipelineBenchmarks>();

public class PipelineBenchmarks
{
  private readonly FrameFilterPipeline _pipeline;
  private readonly CameraConfiguration _configuration;
  private readonly FrameStackResult _frame;

  public PipelineBenchmarks()
  {
    _pipeline = new FrameFilterPipeline(Array.Empty<IFrameFilter>(), NullLogger<FrameFilterPipeline>.Instance);
    _configuration = new CameraConfiguration(false, 1, false, false, 1, 0, Array.Empty<string>());

    var bitmap = new SKBitmap(1936, 1216);
    var exposure = new ExposureSettings(1000, 200, autoExposure: false, autoGain: false);
    _frame = new FrameStackResult(bitmap, bitmap, DateTimeOffset.UtcNow, exposure, null, 1, 0);
  }

  [Benchmark]
  public Task ProcessBaselineAsync()
    => _pipeline.ProcessAsync(_frame, _configuration, CancellationToken.None);
}
```

Running `dotnet run -c Release --project src/HVO.SkyMonitorV5.RPi.Benchmarks` will produce benchmark tables and highlight hot paths. Expand the harness by wiring the real filters through DI when you want full-fidelity measurements.

## Next steps

- Profile on-device with `dotnet-counters` or `dotnet-trace` to observe CPU/concurrency behaviour under load.
- Use the benchmark harness to evaluate changes (for example, alternate image encoders or filter tweaks) before deploying to the observatory hardware.
- Combine runtime diagnostics with BenchmarkDotNet results to zoom in on bottlenecks.
