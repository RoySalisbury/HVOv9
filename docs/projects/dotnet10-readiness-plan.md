# .NET 10 Readiness Plan

## Context
- .NET 10 is expected to ship updated math primitives, wider hardware intrinsics, and native memory abstractions that directly impact the SkyMonitor V5 image pipeline.
- Preparing now allows us to take advantage of those APIs immediately while keeping current .NET 9 behaviour stable.
- Scope focuses on SkyMonitor V5 capture, preprocessing, stacking, and encoding paths, with supporting benchmarks and documentation.

## Objectives
- Reduce managed allocations in projection and stacking math to ease adoption of static-abstract generic math and future vector accelerators.
- Refactor hot loops to operate over spans with clear separation between scalar and vectorised implementations.
- Introduce abstractions around native buffers and preprocessing hooks so we can switch to .NET 10 native pools and math helpers without large code churn.
- Extend instrumentation and benchmarking to validate improvements across Raspberry Pi 5 and x64 targets.

## Workstreams & Tasks

### 1. Projection Math Modernisation (`src/HVO.SkyMonitorV5.RPi/Cameras/Projection`)
**Rationale:** `ProjectionMath` allocates temporary arrays and duplicates vector work. Transitioning to stack-friendly structs prepares us for .NET 10 generic math accelerations.
- [x] Introduce a `struct` (e.g., `ProjectionVector`) that wraps `System.Numerics.Vector3` and exposes static-abstract math operations.
- [x] Replace `double[]` allocations in `ProjectionMath` with the new struct and span-friendly helpers.
- [x] Add unit tests that verify functional parity and guard future refactors.

### 2. Exposure Analyzer Vectorisation (`src/HVO.SkyMonitorV5.RPi/Services/SimpleExposureAnalyzer.cs`)
**Rationale:** The luminance sampler walks the bitmap byte-by-byte. Restructuring now makes it trivial to adopt the wider vector instructions arriving in .NET 10.
- [x] Extract sampling into a dedicated `ExposureAccumulator` with a scalar baseline operating over `Span<byte>`.
- [ ] Introduce feature-gated SIMD paths (e.g., `Vector128`, `Vector256`) behind an abstraction ready for .NET 10 extensions.
- [ ] Benchmark both implementations on Pi 5 and x64 to capture current baselines (`benchmarks/` folder).

### 3. Pixel Conversion Optimisation (`src/HVO.SkyMonitorV5.RPi/Cameras/Zwo/ZwoCameraAdapter.cs`)
**Rationale:** RGB24/RAW16 conversions perform nested index math per pixel. A stride-aware span walker with pending SIMD hooks reduces per-frame CPU cost.
- [x] Refactor `ConvertRgb24ToBgraBitmap` and `ConvertRaw16ToGrayBitmap` to operate on `Span<byte>`/`Span<ushort>` with loop unrolling and guard rails.
- [ ] Introduce partial methods or strategy interfaces for hardware-accelerated paths that .NET 10 will unlock (AVX10, Arm SVE).
- [ ] Add stress tests validating colour correctness and buffer boundaries at multiple resolutions.

### 4. Native Buffer Abstraction (`src/HVO.SkyMonitorV5.RPi/Cameras/Zwo/ZwoCameraAdapter.cs`)
**Rationale:** `FrameBufferLease` directly uses `Marshal.AllocHGlobal`. Wrapping allocation allows adoption of the native memory pools expected in .NET 10.
- [x] Define an `INativeBufferLease` interface (length, pointer, add-ref, release) with the current implementation as the default.
- [x] Centralise allocation/release in a factory that can switch to `System.Buffers.NativeMemoryPool` (or equivalent) when .NET 10 lands.
- [x] Add disposal-focused unit tests to ensure leak-free behaviour under concurrent captures.

### 5. Preprocessing Calibration Pipeline (`src/HVO.SkyMonitorV5.RPi/Pipeline/Preprocessing`)
**Rationale:** `ApplyCalibrations` is currently a stub. Establishing the contract now lets future GPU/Math-heavy operations plug in without structural changes.
- [x] Introduce a calibration pipeline interface taking `Span<float>`/`Span<byte>` views plus hardware capability flags.
- [x] Implement a no-op baseline and wire dependency injection so concrete calibrators can be registered per device.
- [ ] Document extension points for integrating .NET 10 math helpers (tensor APIs, new `MathF` intrinsics).

### 6. Benchmark & Instrumentation Coverage (`benchmarks/`, `artifacts/benchmarks`)
**Rationale:** We need baselines to measure gains after porting to .NET 10 and to ensure regressions are caught early.
- [x] Extend existing BenchmarkDotNet harnesses to cover projection math, exposure sampling, and pixel conversions.
- [ ] Capture baseline metrics on Raspberry Pi 5 and x64 build agents; store summaries under `artifacts/benchmarks`.
- [ ] Add CI hook or documentation notes describing how to run the new benchmarks.

## Validation & Quality Gates
- All refactors must keep existing integration tests passing (`dotnet test src/HVO.SkyMonitorV5.RPi.Tests`).
- New components require unit tests using MSTest with AAA structure and coverage for error paths.
- Record benchmark deltas (before/after) for each workstream prior to switching to .NET 10.
- Ensure structured logging remains consistent; avoid introducing noisy logs in tight loops.

## Timeline & Milestones
- Week 1–2: Projection math struct + exposure accumulator refactor with baseline benchmarks.
- Week 3: ZWO conversion + native buffer abstraction with disposal tests.
- Week 4: Preprocessing calibration contract, benchmark automation, readiness review before .NET 10 adoption window.

## Risks & Mitigations
- **Hardware capability mismatch:** Guard SIMD paths with runtime checks and provide scalar fallbacks.
- **Regression in capture throughput:** Use existing stress suites (`artifacts/stress/`) to validate sustained captures after each change.
- **API churn between .NET 10 previews and GA:** Encapsulate new features behind interfaces so fallback to .NET 9 remains viable until GA.

## Open Questions
- Confirm target .NET 10 variant (LTS vs STS) to align with deployment cadence.
- Determine whether GPU acceleration (Skia, compute shaders) will complement CPU intrinsics in the initial rollout.
- Evaluate if additional teams (e.g., Roof Controller) need similar prep work.
