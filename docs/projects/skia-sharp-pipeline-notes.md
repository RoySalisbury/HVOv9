# SkiaSharp Pipeline Notes

_Last updated: 2025-10-14_

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
