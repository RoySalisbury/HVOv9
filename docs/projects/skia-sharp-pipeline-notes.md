# SkiaSharp Pipeline Notes

_Last updated: 2025-10-15_

## Phase 0 – Baseline Inventory

### Acquisition (Hardware Capture)
- `CameraAdapterBase` orchestrates exposure → acquisition → preprocessing, returning `AdapterFrame` objects that embed `SKBitmap` payloads.
- `CapturedImage` exposes `SKBitmap Image`; ownership of the mutable bitmap is transferred from `AdapterFrame` without conversion to `SKImage`.
- `FramePixelBuffer.FromBitmap` clones bytes (`bitmap.GetPixelSpan()`) when serializing to disk/S3; no zero-copy path.
- No usage of `SKPixmap` or `SKImage` at ingestion time; every acquisition results in a fresh `SKBitmap` allocation and additional copies when duplicated.

### Acquisition (Starfield & Synthetic Sources)
- `Cameras/Rendering/StarFieldEngine.Render` creates a new `SKBitmap(width, height, true)` per call, draws via `SKCanvas`, and returns the mutable bitmap.
- Planet overlays, constellation markers, and random filler stars are redrawn for each invocation; nothing cached between frames.
- Synthetic noise routines (e.g., `SimulatedCpuLoadFrameStackerTests`) mutate `SKBitmap` directly; no shader-based pipeline yet.

### Stacking / Preprocessing
- `RollingFrameStacker` stores queues of `SKBitmap` (copied via `capture.Image.Copy()`) and performs stack math on byte spans.
- Linearization LUTs run against `SKBitmap.GetPixelSpan()`; results are written into another `SKBitmap` (`SKImageInfo.Bgra8888`).
- Calibration artifacts (dark, flat, hot pixel) appear in filter implementations; they mutate the same `SKBitmap` instance cascaded through the pipeline.

### Filter Pipeline
- `FrameFilterPipeline.ProcessAsync` copies the stacked `SKBitmap`, then executes each `IFrameFilter.ApplyAsync` sequentially on the shared bitmap.
- Filters rely on `SKCanvas` wrapping the bitmap; no `SKSurface` snapshots or immutable handoffs.
- Encoding converts back to `SKImage` (`SKImage.FromBitmap`) only when writing out bytes, implying bitmap→image conversion per frame.

### Overlays & Diagnostics
- Filters such as `ConstellationFigureFilter`, `OverlayTextFilter`, `DiagnosticsOverlayFilter`, etc., recreate paints, paths, and geometry per frame.
- No usage of `SKPicture` to record reusable vector overlays; heavy text rendering appears to be rasterized anew for every processed frame.

### Composition & Export
- Composition is effectively fused into the filter pipeline: overlays draw directly onto the same mutable `SKBitmap`.
- `FrameFilterPipeline` encodes the final bitmap via `SKImage.FromBitmap` → `SKData.Encode` (PNG/JPEG) each iteration.
- `FrameExportPublisher` and `AllSkyController` also convert bitmaps to `SKImage` for transmission; export path assumes CPU-backed data.

### Queues, Buffers & Interop
- `FrameStackResult` carries both `StackedImage` and `OriginalImage` as `SKBitmap` references; consumers must manage disposal carefully.
- Remote dispatch tests (`RemoteFramePublisherTests`) instantiate `SKBitmap` for serialization to remote clients.
- No pipeline stage caches immutable `SKImage`; all concurrency control is achieved by copying mutable bitmaps.

## Desired End-State Highlights
- Zero-copy capture via `SKPixmap` + `SKImage` wrappers, maintaining linear `RgbaF16` buffers until export.
- Reusable `SKSurface` pools for preprocessing/filter stages, limiting `SKBitmap` to short-lived CPU mutation windows.
- Overlay assets cached as `SKPicture` and `SKImage`, enabling draw-only composition with minimal per-frame setup.
- Pipeline stages exchange immutable `SKImage` snapshots, improving thread safety and lowering copy pressure.

## Design Considerations & Constraints
- **Target object flow**:
	1. **Capture**: sensor/native buffer → `SKPixmap` wrapper → immutable `SKImage` (linear `RgbaF16`).
	2. **Preprocess**: `SKImage` snapped onto pooled `SKSurface` → snapshot back to `SKImage` after adjustments.
	3. **Filter overlays**: ingest base `SKImage`; parallel filters render to dedicated `SKSurface` → `SKImage` artifacts.
	4. **Composition**: master `SKSurface` draws base `SKImage` + overlay `SKPicture`/`SKImage` → final `SKImage` snapshot.
	5. **Export**: encode `SKImage` to PNG/JPEG; retain high-bit snapshot for archival if required.
- **Lifetime & ownership**: `SKImage` snapshots should behave as reference-counted handles; pipeline stages must dispose intermediate surfaces promptly while allowing final `SKImage` to propagate to queues/exporters.
- **Color pipeline**: prefer linear color space until final export; investigate existing LUT-based linearization in `RollingFrameStacker` and how it maps to F16 surfaces.
- **Threading**: current filters run sequentially on a single bitmap; future surface-based pipeline must respect Skia threading rules (each `SKSurface`/`SKCanvas` confined to creating thread).
- **Memory pressure**: evaluate impact of storing multiple F16 `SKImage` frames in buffers; consider reference counting or eviction policies.
- **Interop boundaries**: confirm downstream consumers (exporters, diagnostics) can accept `SKImage` or require shims returning `SKBitmap` for compatibility during transition.

## Outstanding Questions
- Which filters depend on direct pixel inspection and will require dedicated `SKBitmap.LockPixels()` segments even after refactor?
- What is the acceptable latency/memory trade-off for maintaining both raw and processed `SKImage` snapshots in queues?
- Do external clients rely on BGRA8888 output pre-encoding, or can we uniformly deliver encoded bytes (PNG/JPEG) while keeping F16 masters internal?

## Test Coverage Snapshot
- `FrameFilterPipelineTests` exercises filter sequencing, render context propagation, and telemetry but assumes mutable `SKBitmap` inputs/outputs.
- `RollingFrameStackerTests` validate linear accumulation logic using `SKBitmap` spans; they will need updates once stacking returns immutable `SKImage` snapshots.
- `RemoteFramePublisherTests` serialize `SKBitmap` payloads through export path; no coverage for `SKImage`-based buffers yet.
- Mock camera capture tests focus on adapter lifecycle rather than pixel buffer ownership; gaps remain around multi-threaded handoff and surface-based rendering.
- No automated coverage today for `SKSurface` pooling, `SKPixmap` wrapping, or GPU-backed resources—these will require new tests alongside the refactor.

## Phase 1 – In Progress Notes
- `CameraAdapterBase` now guarantees an immutable `SKImage` snapshot (`AdapterFrame.ImmutableImage`) after post-processing, with ownership transferred to `CapturedImage.ImmutableImage`.
- `CapturedImage`, `FrameStackResult`, and `RawFrameSnapshot` carry optional immutable snapshots; disposal paths updated so exporters and state store manage both bitmap + image lifetimes.
- Raw exporters (`FrameExportPublisher`, remote dispatch encoder, API endpoint) prefer the immutable `SKImage` when present, falling back to transient snapshots only when necessary.
- State store retains the immutable snapshot for latest raw frame, enabling downstream consumers to encode without round-tripping through `SKBitmap`.
- `AdapterFrame` now keeps an optional `SKSurface` alongside the mutable bitmap so post-processing can snapshot directly from the original render without recomputing or allocating an extra `SKImage`.
- Sensor-noise application feeds the updated bitmap back into the retained surface, keeping the immutable snapshot in lockstep with CPU-side mutations and allowing the surface to be disposed immediately after the snapshot is created.
- Mock camera capture now allocates the transport `SKBitmap` first and layers the render surface over the same pixel memory, eliminating the intermediate `SKImage`→`SKBitmap` clone and keeping the render/noise stages truly zero-copy; the immutable snapshot is created with `SKImage.FromPixels` so no additional copy is introduced.
- Outstanding work: introduce zero-copy `SKPixmap` wrapping in adapters; stacking buffers now retain `SKImage` snapshots but need promotion to F16 surfaces in a future pass.
- Mock camera path now renders onto an intermediate `SKSurface` before materializing the transport `SKBitmap`; the immutable snapshot is deferred until post-noise to ensure synthetic sensor noise is captured in the final `SKImage`. Linear F16 surfaces remain a follow-up.
- ZWO adapter wraps zero-copy capture buffers with reference-counted leases and emits linear `SKImage` handles directly from the hardware acquisition stage, eliminating the extra postprocess snapshot and setting the stage for SKPixmap-driven processing.
- Mock camera path uses linear `SKImage` snapshots backed by the rendered bitmap buffer so synthetic pipelines model the same zero-copy handoff as real hardware, regardless of whether sensor noise is enabled.
- `RollingFrameStacker` now buffers immutable snapshots per frame and emits cloned `SKImage` handles with every `FrameStackResult`, allowing downstream services to avoid re-snapshotting while keeping buffer ownership isolated from consumers.
- Processed frame pipeline returns an immutable `SKImage` alongside encoded bytes, with state-store and export paths updated to manage the new lifetime contract.
- Raw frame exports now upload the immutable `SKImage` pixel payload (`application/vnd.hvo.skia.raw`, `*.skimg`) and include a descriptor for width/height/row-bytes so downstream consumers can faithfully reconstruct the F16 buffer without converting to 8-bit PNG first.
- AllSky API clients can request the latest raw frame as `application/vnd.hvo.skia.raw`; the controller forwards the high-bit buffer and emits descriptor headers mirroring the export metadata for zero-copy diagnostics tools.

### Physical ZWO Camera Follow-up (Field Validation)
- Exercise the new `ZwoCameraAdapter` with an actual ASI174MM/ASI174MC body to verify Y8 captures are truly zero-copy (no redundant buffer churn under sustained cadence).
- Validate RAW16 down-conversion path on hardware; capture paired RAW16+Y8 sequences and compare histograms to ensure we are not clipping dynamic range or introducing banding.
- Confirm RGB24 fallback produces accurate colour ordering for Bayer-equipped sensors (cross-check against legacy V4 pipeline captures).
- Measure end-to-end latency and dropped-frame behaviour when the SDK times out; tune `CaptureTimeoutPaddingMs`, retry counts, and logging for real-world exposure lengths (1–60 s).
- Exercise gain/exposure auto modes to ensure control caps from the SDK align with rig configuration and that overrides persist across reconnects.
- Verify cooler/temperature telemetry expectations for cooled bodies; extend the adapter once we have a sensor that exposes `ASI_TEMPERATURE`.
- Stress test ROI/binning reconfiguration to confirm we can downscale or window the sensor without reallocating managed buffers.
- Capture long-run sessions to observe allocator pressure from the array-pool conversions (RAW16/RGB24) and adjust pooling/stride handling if necessary.

## Phase 2 – In Progress Notes

- Capture adapters now surface `SkiaPixelLease` handles through `CapturedImage`, and both synchronous and background paths dispose leases when frames leave the adapter pipeline to avoid lingering `SKPixmap` views.
- Introduced `SkiaSurfacePool` with reusable linear `RgbaF16` surfaces; `RollingFrameStacker` now rents from the pool, draws each candidate frame with weighted `SKPaint`, and snapshots the averaged surface before returning pooled resources.
- Background and synchronous processors were updated to release pixel leases alongside bitmap lifetimes, ensuring state-store ownership mirrors the pre-phase contract.
- Added `FramePreprocessingOrchestrator`, injected into adapters, to stage hardware captures on pooled linear `SKSurface` instances and emit fresh `SKImage`/`SKBitmap` pairs for downstream calibration and stacking.
- Introduced `SkiaImageUtilities` to clone pooled surfaces into independent raster `SKImage` snapshots; preprocessing and stacking now hand back immutable images immediately after CPU mutations, keeping `SKBitmap` allocations scoped to the small windows that still require mutable access.
- Added calibration coverage for both F16 and linear 8-bit capture paths (`FramePreprocessingOrchestratorTests` now exercises RGBA/BGRA8888 round-trips; `RollingFrameStackerTests` verifies linear 8-bit averages match per-channel expectations).
- Added overlay colour-preservation regression for `OverlayTextFilter` ensuring pooled linear gradients remain stable outside the overlay footprint.
- Added gamma validation for sRGB RGBA8888 capture/stack paths (`FramePreprocessingOrchestratorTests`, `RollingFrameStackerTests`).
- Added monochrome luminance regression coverage for preprocessing and stacking to confirm RGB channels remain aligned (`FramePreprocessingOrchestratorTests`, `RollingFrameStackerTests`).
- Next: evaluate overlay asset caching and GPU-backed surface benchmarks ahead of Phase 3 filter refactor.

## Phase 3 – In Progress Notes

- Introduced `IImageFrameFilter` alongside `FilterFrame` so filters can draw against pooled linear `SKSurface` instances while legacy bitmap filters continue to function.
- Refactored `FrameFilterPipeline` to rent `SkiaSurfacePool` surfaces, execute both image and bitmap filters, and return immutable `SKImage` snapshots plus telemetry updates in a single pass.
- Expanded `FrameFilterPipelineTests` with a surface-based regression to ensure `IImageFrameFilter` implementations receive the pooled surface and produce immutable output snapshots.
- Updated diagnostics tests and benchmark harnesses to construct pipelines with a shared `SkiaSurfacePool`, aligning test infrastructure with the new constructor contract.
- Migrated `OverlayTextFilter` to `IImageFrameFilter`, drawing overlays on pooled linear surfaces while keeping legacy bitmap fallback; regression test now validates the FilterFrame path.
- Migrated `DiagnosticsOverlayFilter` to the surface pipeline with updated placement assertions and pooled-surface coverage in `DiagnosticsOverlayFilterTests`.
- Migrated `CardinalDirectionsFilter` to `IImageFrameFilter`, reusing the shared renderer for pooled surfaces and adding a FilterFrame regression in `CardinalDirectionsFilterTests` to verify projector-aligned markers.
- Rolling stacker weighting now leverages per-frame gain via `SKPaint.ColorF` to maintain precise averages across F16, RGBA8888, and BGRA8888 inputs.
- Next up: migrate the remaining overlay/diagnostics filters to `IImageFrameFilter`, cache overlay assets, and explore concurrency/telemetry refinements before enabling parallel execution.

