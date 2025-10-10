# SkyMonitor Frame Context & Rig Integration Plan

_Last updated: 2025-10-07_

## Goals
- Preserve full rig metadata (sensor, lens, orientation) alongside every captured frame.
- Share the exact `StarFieldEngine` and projector instance used during capture with downstream filters via a dedicated `FrameContext`.
- Remove the `IRenderEngineProvider` singleton in favor of explicit context passing throughout the pipeline.
- Simplify dependency injection so camera adapters own rig/projector construction while remaining extensible for future hardware implementations.

## Guiding Principles
1. **Single source of truth** – the camera adapter creates the `FrameContext` once per capture; all later stages consume the same instance.
2. **Explicit ownership** – whichever component allocates the engine is responsible for disposing it after filters complete.
3. **Minimal globals** – prefer passing context objects over ambient singletons unless multiple subsystems genuinely require shared state.
4. **Drop-in extensibility** – adding a new camera or filter should only require participating in the context contract, not rewiring DI.

## Phased Updates

### Phase 1 – Data Model
- [x] Introduce a `FrameContext` record containing:
  - `RigSpec Rig`
  - `IImageProjector Projector`
  - `StarFieldEngine Engine`
  - Capture metadata (`DateTimeOffset Timestamp`, boresight, flip flags, etc.)
- [x] Extend `CameraFrame` (or create `CaptureResult`) to carry the optional `FrameContext`.
- [x] Update `FrameStackResult` to keep the latest non-null `FrameContext`.

### Phase 2 – Camera Adapter
- [x] Inject rig configuration directly into `MockCameraAdapter` (and future adapters) by resolving `RigSpec` from configuration.
- [x] Use `RigFactory.CreateProjector` per frame and build the `StarFieldEngine` from rig + capture parameters.
- [x] Populate the `FrameContext` when constructing the returned `CameraFrame`.
- [x] Remove calls to `IRenderEngineProvider.Set` once filters are context-aware.

### Phase 3 – Stacker & Pipeline
- [x] Ensure `RollingFrameStacker` copies/merges `FrameContext` into the aggregated frame.
- [x] Update `FrameFilterPipeline.ProcessAsync` to construct a `FrameRenderContext` from the stacked frame’s context and pass it to the new filter overload.
- [x] Dispose of `FrameContext.Engine` after filters finish (or hand disposal back to the adapter via a callback).
- _Considerations (addressed 2025-10-09):_
  - The stacker now snapshots per-frame rig metadata and resets the buffer if a mismatch is detected, ensuring we never blend frames from divergent contexts.
  - FrameContext disposal is centralized inside the filter pipeline, eliminating duplicate dispose paths in the capture and background services.

### Phase 4 – Filters & DI Cleanup
- [x] Migrate filters to rely on the supplied `FrameRenderContext`.
  - [x] CardinalDirectionsFilter uses projector metadata for centering and rotation.
  - [x] OverlayTextFilter pulls rig/location/timezone from the render context.
  - [x] CircularApertureMaskFilter bases radius/centroid on projector + horizon padding.
  - [x] CelestialAnnotationsFilter resolves star catalog and projector coordinates via context.
  - [x] ConstellationFigureFilter moves to context-aware geometry calculations.
  - [x] Diagnostics overlay shares context for sensor telemetry.
- [x] Remove the legacy `IRenderEngineProvider` singleton and corresponding constructor parameters.
- [x] Audit DI registrations in `Program.cs` and strip any now-unused singletons.

### Phase 5 – Verification & Docs
- [x] Add unit coverage for the pipeline ensuring `FrameRenderContext` travels end-to-end.
  - [x] Create pipeline tests that process a stacked frame and assert `FrameRenderContext` disposal and filter telemetry. (`FrameFilterPipelineTests`, `RollingFrameStackerTests`)
- [x] Add performance-focused tests that record stacking + filter timings for future regression tracking. (`BackgroundFrameStackerServicePerformanceTests` validates telemetry aggregation.)
  - [x] Evaluate whether to keep timing tests in MSTest or split into a dedicated performance benchmark suite for cleaner separation. _Decision:_ keep lightweight telemetry assertions in MSTest for now; revisit if runtime measurements become flaky._
- [x] Refresh README/sequence diagrams to reflect the new context-oriented flow.
- [x] Perform full `dotnet build` and targeted smoke tests.

### Phase 6 – Performance Benchmark Suite
- [x] Design a standalone performance benchmarking project (evaluate BenchmarkDotNet vs. custom harness). _Decision:_ use BenchmarkDotNet in `HVO.SkyMonitorV5.RPi.Benchmarks` with configurable job profiles._
  - [x] Identify critical pipeline hot paths (stacking, filter execution, capture loop) and define representative scenarios. _Current coverage: rolling stacker accumulation, filter pipeline rendering, mock capture, and end-to-end pipeline runs._
  - [x] Ensure benchmarks can run against synthetic data without hardware dependencies. (Syntehetic data can include the Mock camera since it is not tied to actual hardware).
- [x] Integrate benchmark outputs into CI or developer tooling (decide on cadence and thresholds). _CI now runs a nightly/pull-request smoke benchmark job with short iteration counts and uploads the summary artifacts._
- [x] Document benchmark setup and interpretation guidance in the repository. (`src/HVO.SkyMonitorV5.RPi.Benchmarks/README.md`)

### Phase 7 – Wrap-Up & Refactoring
- [x] Audit SkyMonitor v5 projects for redundant or unused classes and remove or consolidate them. _Removed obsolete `MockFisheyeCameraAdapter`, unused `SkySimulationMath`, and legacy `CameraFrame` model; updated docs to reference `CapturedImage`._
- [x] Trim unused assets (images, docs, sample data) related to the old context pipeline. _Reviewed SkyMonitor docs/assets; remaining diagrams still describe the active flow, no extra files required._
- [x] Refresh solution/solution-filter entries to ensure only active projects are tracked. _Confirmed `HVOv9.slnf` indexes every active project except the iPad target per cloud-build constraints._
- [x] Update documentation (README, diagrams) to reflect post-cleanup architecture.
- [x] Run final build/test/benchmark smoke to confirm no regressions before closing the initiative. _`dotnet build` + benchmark smoke (`*EndToEndPipeline*`, 1 launch/warmup/iteration) both succeeded on 2025-10-09._
- [x] Capture a five-minute benchmark soak to observe sustained buffer/GC behavior. _`IterationCount=300`, `WarmupCount=3`, `LaunchCount=1`; mean stabilized at ~55–61 ms for stacking configurations with allocations steady at 126–206 KB per op._

## Open Questions / Follow-Ups
- How should disposal behave when a capture fails mid-pipeline (e.g., cancellation)? (Current implementation disposes in both the pipeline and capture loop finally blocks; confirm no regressions in long-running sessions.)
- Do we need to pool projector instances for performance, or is per-frame allocation acceptable?
- Should filters be allowed to mutate the `FrameContext` (e.g., adding computed lookup tables), or keep it immutable?
- What telemetry should we log when the context is missing or partially populated?

## Post-Background-Stacker TODOs
- _Moved to solution-wide TODO tracking (see `docs/TODO.md`)._

## Success Criteria
- Filters can rely exclusively on `FrameRenderContext` without DI lookups.
- Removing `IRenderEngineProvider` does not regress existing overlay behavior.
- New camera adapters can opt-in by populating `FrameContext` without touching the pipeline.

---
This document will track implementation progress; update checkboxes and notes as each phase lands.
