# SkiaSharp Pipeline Transition Plan

> Branch: `feature/skia-sharp-pipeline`
>
> Use the checkboxes below to track progress. Keep items unchecked until fully validated (code, tests, docs, benchmarks).

## Phase 0 – Baseline & Instrumentation

- [x] Inventory current Skia usages across acquisition, processing, filters, composition, export.
- [x] Draft living design notes covering desired object flow, lifetimes, and threading constraints.
- [x] Add benchmark harnesses for capture, single filter, and full composite paths.
- [x] Ensure existing automated tests cover critical image paths; log any gaps to be filled later.

## Phase 1 – Acquisition & Raw Frame Capture

- [x] Update real camera ingestion to wrap native buffers with `SKPixmap` → `SKImage` (`RgbaF16`, linear).
- [x] Render synthetic/starfield frames on linear `SKSurface` and snapshot to `SKImage` with noise applied.
- [x] Normalize capture abstractions to expose immutable `SKImage` plus metadata to downstream consumers.
- [x] Update raw-frame S3 uploader to persist high-bit `SKImage` masters without premature 8-bit conversion.

> Status: ✅ Completed 2025-10-14. All follow-up work rolls into Phase 2.

## Phase 2 – Preprocessing & Calibration

- [x] Convert capture adapters to expose zero-copy `SKPixmap` leases, eliminating transient `SKBitmap` clones.
- [x] Promote `RollingFrameStacker` (and supporting helpers) to accumulate on pooled linear `SKSurface` instances.
- [x] Introduce preprocessing orchestrator that pulls `SKImage` into pooled `SKSurface` instances for demosaic/calibration.
- [x] Reserve `SKBitmap` for CPU-only steps; convert back to `SKImage` immediately after mutation.
- [x] Expand calibration tests to verify scientific accuracy and bit-depth preservation post-refactor (added F16 + linear 8-bit color-preservation coverage in `FramePreprocessingOrchestratorTests` and `RollingFrameStackerTests`).

## Phase 3 – Filter Pipeline Refactor

- [x] Define filter interface accepting `SKImage` and emitting `SKImage`, with helpers for safe pixel mutation. (`IImageFrameFilter`, `FilterFrame`, and pooled `SkiaSurfacePool` integration in `FrameFilterPipeline`.)
- [x] Move filter implementations to dedicated `SKSurface` workflows, sharing cached shader state. (OverlayTextFilter, DiagnosticsOverlayFilter, CardinalDirectionsFilter, ConstellationFigureFilter, CelestialAnnotationsFilter, and CircularApertureMaskFilter now target `FilterFrame`; legacy helpers audited for `SKBitmap` dependencies.)
- [ ] DEFER THIS ACTION TO PHASE 8. Enable configuration-driven parallel execution with bounded schedulers and thread-safe resource handling. Big question here is if the shared StarFieldEngine and projector can be utilized safely or if we need to stick with synchronous filter execution.
- [x] Add per-filter unit tests comparing output to legacy expectations. (OverlayText, Diagnostics, Cardinal Directions, Constellation Figures, Celestial Annotations, and Circular Mask filters now have surface-path regressions under `HVO.SkyMonitorV5.RPi.Tests`.)

## Phase 4 – Overlay Asset Strategy

- [ ] Record reusable vector overlays as cached `SKPicture` instances.
- [ ] Pre-rasterize expensive/textual overlays to `SKImage` with invalidation hooks for configuration changes.
- [ ] Add diagnostics to verify overlay alignment and antialiasing on high-bit surfaces.

## Phase 5 – Composition & Frame Queues

- [ ] Centralize composition on linear `SKSurface` drawing base `SKImage`, overlays, and masks deterministically.
- [ ] Update frame buffers/queues to store only `SKImage` references plus metadata.
- [ ] Implement regression tests ensuring deterministic blends and expected performance characteristics.
- [ ] Capture profiling metrics before/after to validate improvements.

## Phase 6 – Export & Post-Processing Handoff

- [ ] Provide encoders that convert high-bit `SKImage` to delivery formats (PNG/JPEG) only at export time.
- [ ] Update post-pipeline S3 uploader to choose archive/delivery formats while retaining linear masters when needed.
- [ ] Validate outputs via golden-image comparisons and confirm metadata fidelity.

## Phase 7 – Validation, Tooling & Rollout

- [ ] Broaden automated coverage (unit, integration, end-to-end) to stress the new pipeline.
- [ ] Re-run benchmark suite, publish results in `docs/performance-benchmarks.md`.
- [ ] Add feature toggles/fallbacks with monitoring to allow staged rollout.
- [ ] Prepare operational runbook updates covering cache warm-up and resource expectations.

## Appendix – Open Questions / Follow-Ups

- [ ] Benchmark GPU-backed surfaces vs CPU-only for overlays (if applicable).
- [ ] Evaluate memory pressure of storing high-bit `SKImage` masters in queues at target frame rates.
- [ ] Confirm third-party consumers (e.g., analytics jobs) can ingest new SKImage-based outputs without modification.
- [x] Add overlay filter colour-preservation regression (e.g., `OverlayTextFilter` on pooled linear surfaces) (`OverlayTextFilterTests`).
- [x] Validate gamma correctness for sRGB capture inputs by comparing linear vs legacy pipelines (`FramePreprocessingOrchestratorTests`, `RollingFrameStackerTests`).
- [x] Cover monochrome sensor stacking with luminance-only gradient fixtures to confirm weighting mirrors colour path (`FramePreprocessingOrchestratorTests`, `RollingFrameStackerTests`).
