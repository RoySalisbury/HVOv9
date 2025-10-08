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
- [x] Inject `IRigProvider` into `MockFisheyeCameraAdapter` (and real adapters).
- [x] Use `RigFactory.CreateProjector` per frame and build the `StarFieldEngine` from rig + capture parameters.
- [x] Populate the `FrameContext` when constructing the returned `CameraFrame`.
- [x] Remove calls to `IRenderEngineProvider.Set` once filters are context-aware.

### Phase 3 – Stacker & Pipeline
- [x] Ensure `RollingFrameStacker` copies/merges `FrameContext` into the aggregated frame.
- [x] Update `FrameFilterPipeline.ProcessAsync` to construct a `FrameRenderContext` from the stacked frame’s context and pass it to the new filter overload.
- [x] Dispose of `FrameContext.Engine` after filters finish (or hand disposal back to the adapter via a callback).

### Phase 4 – Filters & DI Cleanup
- [x] Migrate filters (`CelestialAnnotationsFilter`, `CardinalDirectionsFilter`, etc.) to rely on the supplied `FrameRenderContext`.
- [x] Remove the legacy `IRenderEngineProvider` singleton and corresponding constructor parameters.
- [x] Audit DI registrations in `Program.cs` and strip any now-unused singletons.

### Phase 5 – Verification & Docs
- [ ] Add unit coverage for the pipeline ensuring `FrameRenderContext` travels end-to-end.
- [ ] Refresh README/sequence diagrams to reflect the new context-oriented flow.
- [x] Perform full `dotnet build` and targeted smoke tests.

## Open Questions / Follow-Ups
- How should disposal behave when a capture fails mid-pipeline (e.g., cancellation)? (Current implementation disposes in both the pipeline and capture loop finally blocks; confirm no regressions in long-running sessions.)
- Do we need to pool projector instances for performance, or is per-frame allocation acceptable?
- Should filters be allowed to mutate the `FrameContext` (e.g., adding computed lookup tables), or keep it immutable?
- What telemetry should we log when the context is missing or partially populated?

## Success Criteria
- Filters can rely exclusively on `FrameRenderContext` without DI lookups.
- Removing `IRenderEngineProvider` does not regress existing overlay behavior.
- New camera adapters can opt-in by populating `FrameContext` without touching the pipeline.

---
This document will track implementation progress; update checkboxes and notes as each phase lands.
