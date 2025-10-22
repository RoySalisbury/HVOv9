# Phase 3.3 Review – Remote Dispatch & Background Stacker

_Last updated: 2025-10-12 (afternoon)_

## High-Level Flow

- **Capture pipeline** (`AllSkyCaptureService`)
  - **Camera setup → exposure request**
    - Starts the active rig via `IRigAcquisitionAdapter` and pulls the latest `CameraPipelineOptions` snapshot (including async toggle and stacker enablement).
    - Delegates to the injected `IExposureController` to synthesize the next `ExposureSettings` based on the most recent analysis, rig state, and configuration. This happens on the capture loop thread before any frame acquisition.
  - **Frame acquisition & exposure analysis feedback loop**
    - Captures the frame asynchronously through the rig adapter. Regardless of the async processing mode, this capture stage runs on the main loop so the service can measure latency and pacing consistently.
    - Immediately after a successful capture, synchronously invokes the optional `IExposureAnalyzer` to compute luminance metrics and recommended exposure. The analysis result is applied back to the controller via `ApplyAnalysis`, ensuring the very next `CreateNextExposure` call reflects the latest scene brightness.
    - Publishes analysis telemetry to `IFrameStateStore` so the UI can surface current lighting and suggested settings.
  - **Pre-queue preparation**
    - Pre-computes `CaptureSizeBytes` to decouple queue telemetry from SkiaSharp lifetime management.
    - Packages a `RemoteFrameEnvelope` for dispatch and invokes the remote publisher (always awaited). Remote dispatch runs before we decide between sync vs async local processing to keep external copies in step with internal flow.
  - **Process routing (sync vs async)**
    - If `EnableAsyncProcessing` is true, the service pushes a `ProcessingWorkItem` onto a single-reader bounded channel; the capture loop handles pacing while a dedicated worker calls `ProcessCapturedFrameAsync`. Queue metrics/telemetry are published from the worker thread.
    - In synchronous mode, `ProcessCapturedFrameAsync` runs inline on the capture loop thread. Both modes share pacing logic (`ApplyCapturePacing`) to account for background stacker pressure and exposure penalties before the next capture.
  - **Framing to stacker buffer**
    - During processing, the service decides whether to enqueue the prepared `StackingWorkItem` into the background stacker (when enabled) or run the stack/filter pipeline synchronously, falling back when enqueue attempts are rejected. Telemetry distinguishes enqueue time, stack time, and filter time so upstream pacing can adapt.
- **Background stacker service** (`BackgroundFrameStackerService`)
  - Maintains a bounded channel with adaptive capacity; swaps channels when pressure thresholds are crossed or configuration changes require a new queue.
  - Tracks queue depth, peak depth, and memory consumption purely from the cached `CaptureSizeBytes` to prevent SkiaSharp access after disposal.
  - Publishes telemetry to the frame state store and meters for UI/observability.
  - Guarantees safe disposal of queued items even when tests or stress rigs push null bitmaps.
- **Remote dispatch pipeline** (`RemoteFramePublisher` + `IRemoteFrameEncoder`)
  - Normalizes `CameraPipelineOptions.RemoteDispatch` settings and acts as the sole publishing surface from the capture pipeline.
  - Uses the injected `IRemoteFrameEncoder` (currently `SkiaRemoteFrameEncoder`) to convert raw `SKBitmap` frames into the configured lossless/lossy format before handing the payload to MinIO/S3.
  - Uploads to MinIO-backed S3 using the new `IMinioClientProvider`, stamping objects with the correct content type, file extension, and telemetry headers.
  - Returns typed `RemoteDispatchResult` outcomes for the capture service to both log and surface through `RemoteDispatchStatus`.
- **Observability & UI wiring**
  - Frame state store is updated with remote dispatch, queue, and background stacker telemetry, enabling the dashboard to reflect live status.
  - New gauges and counters were registered for queue depth, memory usage, stack/filter durations, queue fill percentage, and remote dispatch health, all exported via Prometheus.
  - Diagnostics UI now displays remote dispatch configuration badges (enabled/disabled/warnings) with inline validation when required fields are missing.
  - The home dashboard now surfaces remote dispatch success rate, attempt counts, latency snapshots, and payload format mix alongside the latest outcome.

## Current Testing Coverage

- **Unit tests**
  - `RemoteFramePublisherTests` verify disabled, misconfigured S3, and happy-path MinIO uploads (including encoder integration via dependency injection).
  - `BackgroundFrameStackerServicePerformanceTests` exercise adaptive queue swaps and queue telemetry without dereferencing disposed bitmaps.
- **Integration/stress**
  - SkyMonitorV5.RPi test suite now passes after removing all direct SkiaSharp calls during drain/disposal.
  - Stress harness (`HVO.SkyMonitorV5.RPi.Stress`) completed two consecutive release runs (`--duration 10 --sample 15`) on 2025-10-11, producing summaries `20251011_191401_stress-summary.json` and `20251011_195503_stress-summary.json`. Queue pressure still peaks at 28/36 (≈75%), but the harness no longer exits early.

## Notes & Considerations

- **Bitmap lifetime**: We now rely entirely on cached byte sizes for queue memory metrics. Any future producer of `StackingWorkItem` must set `CaptureSizeBytes`; otherwise queue pressure metrics will read as zero.
- **Adaptive queue tuning**: Cooldown windows and bucket thresholds are static. Observed telemetry during stress runs suggests we may need configurable high/low pressure durations in later phases.
- **Remote dispatch extensibility**: S3/MinIO is the only defined mode today. The options class anticipates additional fan-out transports (e.g., RabbitMQ or WebSocket fanouts) but their behavior is not yet implemented.
- **Encoding flexibility**: The encoder hook enables PNG, JPEG, and BMP payloads; TIFF/FITS are marked unsupported and throw, surfacing as remote dispatch failures. We should decide on FITS support requirements before phase 4.
- **Status surface**: `RemoteDispatchStatus` currently reflects only the last dispatch attempt. If we need historical insight, we should plan a rolling buffer or telemetry event stream.
- **Stress telemetry**: Latest stress runs finish cleanly yet sustain elevated background stacker pressure (average depth ≈26/36, peak 28). Continue monitoring to ensure the adaptive pacing keeps queue fill below safety thresholds.

## Follow-Up Tasks (Phase 3.3 Continuation)

1. **MinIO production hardening**
  - Add retry/backoff and timeout policies around the MinIO client, and surface structured diagnostics (bucket, key, latency) to telemetry.
  - Store payload metadata (format, size) in a dedicated manifest header and validate downstream consumers can honor the new image formats.
2. **Remote dispatch health telemetry**
  - _Deferred to SkyMonitorV5 Data Store project_: extend `RemoteDispatchStatus` (or successor persistence layer) to capture the last _n_ attempts so operators can see intermittent issues without digging into logs.
  - Evaluate whether latency percentile gauges are needed in addition to the new average/peak exports to support alerting thresholds.
3. **Background stacker stress hardening**
  - Analyze sustained high queue pressure during stress runs and determine whether capacity adjustments or pacing tweaks are warranted before they saturate in production.
  - Add debug-level instrumentation around adaptive queue adjustments to catch oscillation or channel swap races.
4. **Result<T> integration**
  - The remote publisher currently returns simple POCO results. Align with the workspace `Result<T>` pattern for consistent error handling across services.
5. **Configuration UX**
  - Extend the diagnostics panel into an editable admin workflow so operators can update remote dispatch settings (bucket, endpoint, credentials, image format) in-place.
  - TODO: Extract the remote dispatch configuration editor into a shared component so SkyMonitor, Roof Controller, and future observatory apps can reuse the same UX without duplicating logic.
6. **Post-processing export hook**
  - Introduce an extensibility point after filter/pipeline completion to persist the final processed frame to local storage and/or trigger a secondary S3 upload. This must run independently of the raw-frame remote dispatch so local telemetry and pacing remain unaffected.
7. **FITS/TIFF encoder support**
  - Evaluate library options for lossless scientific formats. Either plug in a new encoder implementation or document why PNG is sufficient for downstream consumers before phase 4.

## Phase 3.3 Exit Checklist (before moving to Phase 4)

- ✅ Raw frame dispatch runs through MinIO with configurable image formats and telemetry headers.
- ✅ Stress harness completes 10s/15 sample runs without exiting early; elevated queue pressure documented for continued monitoring.
- ✅ Remote dispatch telemetry visualized in dashboard (success, failure, latency, payload format trends).
- ✅ Remote dispatch gauges exported for Prometheus scraping (success rate, latency snapshots, outcomes, payload bytes).
- ✅ Processed-frame export hook implemented (processed exports flow through filesystem sink with optional S3 destinations once credentials are supplied).
- ✅ FITS/TIFF encoder work deferred to a future project (tracked in `docs/TODO.md`).
- ☐ Configuration UX updated so operators can safely enable/disable remote dispatch and choose payload formats (deferred to Phase 4 configuration UI work).

## Open Questions

- Do we expect multi-tenant dispatch targets (multiple buckets/exchanges) in later phases? This will influence envelope payload design.
- What serialization format should the S3 payload use, and how will downstream consumers authenticate and hydrate it?
- Should the background stacker emit per-frame detail events for observability tools, or is the current aggregated telemetry sufficient?

## Deferred to the SkyMonitorV5 Data Store Project

- Persist remote dispatch and stacker telemetry history beyond the in-memory buffers, including retention policies and query APIs.
- Consolidate SQLite EF Core contexts (telemetry + third-party catalogs) into the new `HVO.SkyMonitorV5.Data` project and manage migrations there.
- Standardise runtime data directories (e.g., `/var/hvo/datastores/`) so container volumes can mount and swap database files post-deployment.
