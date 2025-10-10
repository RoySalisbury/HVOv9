# HVO SkyMonitor v5 Benchmarks

This project hosts performance benchmarks for the SkyMonitor v5 capture and processing pipeline. The goal is to provide quick, repeatable measurements that highlight CPU and memory hotspots outside of CI-only scenarios.

## Scenarios

| Benchmark | Purpose | Notes |
|-----------|---------|-------|
| `RollingFrameStackerBenchmarks.AccumulateFrame` | Measures the cost of accumulating synthetic frames with varying stack depths. | Exercises `RollingFrameStacker` using large synthetic images to surface buffer pressure and disposal overhead. |
| `FrameFilterPipelineBenchmarks.ProcessFrameAsync` | Measures the filter pipeline with configurable synthetic filters and frame sizes. | Uses in-memory filters that simulate overlay drawing to stress bitmap mutation and telemetry recording. |
| `MockCameraCaptureBenchmarks.CaptureFrameAsync` | Measures the mock camera adapter capture loop end-to-end. | Validates rig/context construction and frame buffering costs without physical hardware. |
| `EndToEndPipelineBenchmarks.CaptureStackProcessAsync` | Runs capture + stack + filter pipeline combinations. | Helps estimate sustained memory/GC pressure and IO across realistic frame counts and filter stacks. |

The synthetic frame generator shares the same rig/projector setup as the main application and uses the mock starfield rendering pipeline, so the results align closely with real workloads while remaining hardware-independent.

## Running locally

```bash
cd ../../..
DOTNET_ENVIRONMENT=Benchmark dotnet run --project src/HVO.SkyMonitorV5.RPi.Benchmarks/HVO.SkyMonitorV5.RPi.Benchmarks.csproj -c Release
```

### Long-running or stress configurations

BenchmarkDotNet already supports command-line overrides such as `--launchCount`, `--warmupCount`, and `--iterationCount`. In addition, the runner honors a set of environment variables that make it easy to script sustained integrations:

| Variable | Purpose | Default |
|----------|---------|---------|
| `HVO_BENCH_WARMUP_COUNT` | Number of warmup iterations before measurements. | `1` |
| `HVO_BENCH_ITERATION_COUNT` | Number of measurement iterations. | `10` |
| `HVO_BENCH_LAUNCH_COUNT` | Number of benchmark process launches. | BenchmarkDotNet default |
| `HVO_BENCH_INVOCATION_COUNT` | Explicit invocation count per iteration. | BenchmarkDotNet default |
| `HVO_BENCH_UNROLL_FACTOR` | Unroll factor for throughput tuning. | BenchmarkDotNet default |

Example: run the end-to-end pipeline for a prolonged soak to capture GC/IO behavior.

```bash
cd ../../..
HVO_BENCH_WARMUP_COUNT=3 \
HVO_BENCH_ITERATION_COUNT=200 \
HVO_BENCH_LAUNCH_COUNT=2 \
DOTNET_ENVIRONMENT=Benchmark \
dotnet run --project src/HVO.SkyMonitorV5.RPi.Benchmarks/HVO.SkyMonitorV5.RPi.Benchmarks.csproj -c Release -- --filter '*EndToEnd*'
```

The `DOTNET_ENVIRONMENT` variable is optional but can help distinguish benchmark runs in logs. BenchmarkDotNet will emit detailed reports under the `BenchmarkDotNet.Artifacts` folder.

## Extending the suite

- Add new benchmark classes under `Benchmarks/` with `[MemoryDiagnoser]` to capture allocation data.
- Reuse `BenchmarkDataFactory` to produce context-aware frames without hardware dependencies.
- For full end-to-end tests (camera + stacking + filters), consider composing multiple components inside a single benchmark method; BenchmarkDotNet supports `[IterationSetup]` to warm up more complex graphs.
- When a new benchmark highlights a regression, capture the generated `.md` report in the PR or issue to document the before/after comparison.

## Future ideas

- Export summary metrics (mean, allocations, P95) to CSV for ingestion into the existing observability dashboards.
- Wire a CI job that runs a smoke subset (e.g., `--filter *Stacker*`) on a nightly cadence, while developers can run the full sweep locally for deeper investigations.
