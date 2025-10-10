# SkyMonitor Background Stacker Design

_Last updated: 2025-10-09_

## Objective

Move the RollingFrameStacker work off the capture loop thread so that capture cadence is governed by the configured interval rather than the per-frame stacking cost. The background worker must preserve current stacking semantics (sliding window of the most recent frames) while providing hooks for future consumers (disk writer, timelapse encoder/exporter, livestream encoder, meteor detection, AI inspection).

## Working Agreements

- Focus all implementation work within the `HVO.SkyMonitorV5.RPi` project, only touching other projects when they are direct dependencies required by SkyMonitor V5.
- Defer adding or expanding test coverage until the end of the current phase unless a targeted test is necessary to validate an in-flight change.

## Success Criteria

- Preserve the existing stacking behavior: stacked frame includes the most recent `StackingFrameCount` images that match the current frame dimensions and configuration.
- Maintain capture cadence: the capture loop should only block when explicitly applying backpressure; the background stacker must not grow memory without bounds.
- Provide deterministic shutdown: buffers and pending work are drained/disposed when the host stops.
- Supply metrics/logging so we can observe backlog depth, processing duration, and dropped-frame counts.

## Proposed Architecture

### 1. Producer/Consumer Channels

- Introduce `Channel<StackingWorkItem>` with a bounded capacity (~2× `StackingFrameCount`).
- The capture loop becomes the producer: each `CapturedImage` and associated metadata is posted to the channel.
- A dedicated background task (`BackgroundStackerWorker`) consumes the channel sequentially, applying stacking and handing off results.

### 2. Work Item Contract

```csharp
internal sealed record StackingWorkItem(
    int FrameNumber,
    CapturedImage Capture,
    CameraConfiguration ConfigurationSnapshot,
    DateTimeOffset EnqueuedAt);
```

- `FrameNumber` is monotonic for logging/diagnostics.
- `ConfigurationSnapshot` ensures the worker stacks with the exact settings that were active when the frame was captured, even if configuration changes are applied later.

### 3. Background Worker Flow

1. Await `Channel.Reader.ReadAsync` with cancellation.
2. Push the bitmap into the internal buffer (same behavior as current `RollingFrameStacker`).
3. Perform stacking when buffer count ≥ `StackingFrameCount`.
4. Emit a `StackingResult` (stacked bitmap + metadata) to the next pipeline stage.

```
Capture Loop --> Channel --> BackgroundStacker --> Filter Pipeline --> State Store --> Downstream consumers
```

### 4. Handoff to Filters

Two options:

- **Inline Filters:** run the existing filter pipeline inside the background worker before publishing the processed frame. Simpler integration; capture loop only waits on channel backpressure.
- **Separate Stage:** output `StackingResult` to a second channel processed by another worker that runs the filters. Offers more isolation but adds complexity. **Initial plan:** keep filters in the background stacker worker to minimize churn, revisit if filter costs warrant separate staging.

### 5. Backpressure & Drop Strategy

- Channel is bounded; when full, `WaitToWriteAsync` blocks the capture loop, enforcing backpressure.
- Allow an optional configuration flag to switch to `DropOldest` behavior: if enabled, the newest frame replaces the oldest unprocessed item instead of blocking capture.
- Collect metrics: maximum queue length, total dropped frames, average wait time.

### 6. Disposal & Shutdown

- Worker watches the host cancellation token.
- On shutdown, complete the channel, drain remaining items, dispose the internal bitmap buffer, and release any in-flight stacked images using the existing `RollingFrameStacker.Dispose` helper.

### 7. Memory Footprint Options

- Keep the working buffer in raw `SKBitmap` form by default to maximize stacking throughput.
- Add an optional "lossless compression" mode that compresses buffered frames (e.g., `ZlibStream`, `ZstdNet`) when they are enqueued and decompresses prior to stacking.
   - Guard behind a configuration flag (`StackerCompressionMode = None|Lossless`) so high-performance scenarios avoid the overhead.
   - Measure compression ratio vs CPU cost using representative frames (night sky, moonlit, cloudy) before enabling by default.
   - Ensure compression is applied off the capture loop thread to avoid extending capture latency.
- Track buffer memory usage metrics so we can surface alerts if the queue approaches configured limits even with compression enabled.

## Implementation Plan

1. **Infrastructure Setup**
   - Add new worker class (`BackgroundFrameStackerService`) that implements `IHostedService`.
   - Register the worker in `Program.cs` and inject it alongside the existing `AllSkyCaptureService`.
   - Establish auto-restart semantics with configurable backoff (`RestartDelaySeconds`) so the worker recovers from unexpected failures.

2. **Message DTOs**
   - Create `StackingWorkItem`, `StackingResult`, and optional telemetry structs.

3. **Capture Loop Changes**
   - Replace direct `_frameStacker.Accumulate` call with channel `WriteAsync`.
   - Capture loop awaits only the enqueue (respecting cancellation); stacking moves to worker.

4. **Background Worker Logic**
   - Consume channel, call `_frameStacker.Accumulate`, execute filter pipeline, update `FrameStateStore`.
   - Ensure locking/ownership of bitmaps so only one path disposes them.

5. **Backpressure Configuration**
   - Extend `CameraPipelineOptions` with `StackerQueueCapacity` and `CaptureQueueOverflowPolicy`.
   - Emit debug/trace logs when the queue fills or frames are dropped.

6. **Metrics & Logging**
   - Add structured logs for queue depth, stack duration, and enqueued-to-processed latency.
   - Optionally wire counters into `FrameStateStore` for UI inspection.
   - Include memory usage estimates (bytes in queue, compression hit rate) to evaluate whether compression/queue sizing needs adjustment.

   ✅ Queue depth, latency, and drop metrics are now tracked in `BackgroundFrameStackerService`, published through `FrameStateStore`, and surfaced on the SkyMonitor UI.
   ✅ Queue memory consumption and peak queue depth are tracked, exposed via the status API, and rendered on the dashboard.
   ✅ Diagnostics metrics endpoint (`/api/v1.0/diagnostics/background-stacker`) added for external tooling and future dashboard widgets.
   ✅ Metrics are exported through `System.Diagnostics.Metrics` (counters, histograms, gauges) so Prometheus/OpenTelemetry collectors can scrape stacker performance.

## Follow-Up Backlog

- Resolve the frequent filter annotation warnings (`DestroyAllActors`, `DestroyLowStamp`) so the capture logs stay clear of false alarms.
- Wire Prometheus/OpenTelemetry exporters to publish the existing meter instrumentation and then run sustained stress/backpressure sessions to validate queue behavior under load.
- ✅ Expand the diagnostics dashboard with backend-provided historical windows once the exporter is available, so charts can show longer trendlines beyond the live in-memory samples.
- Add camera capability metadata (Color/Monochrome, Cooled, etc.) to the SkyMonitor UI alongside the pipeline details.
- Schedule longer live runs to compare UI telemetry with raw log output and confirm queue pressure thresholds behave as expected.

7. **Testing**
   - Unit-test queue overflow policies with a fake stacker.
   - Integration test covering capture loop + worker to verify stacked frame content and timing.
   - Stress test: simulate high-frequency capture to ensure backpressure or drop policy behaves as expected.

8. **Documentation & Deployment Notes**
   - Update `docs/performance-benchmarks.md` with the background stacker experiment results.
   - Document new configuration knobs in `README` and `appsettings` templates.
   - Capture compression trade-offs, default settings, and recommended hardware profiles.
   - Outline container deployment topologies (single container vs split capture/stacker/consumer services) for future scaling.
   - Describe operational tuning for queue capacity, overflow policy, compression mode, and restart delay.

9. **Containerization Strategy (Future)**
   - Keep the initial implementation inside the existing SkyMonitor service for simplicity.
   - Design the architecture so the capture loop, stacker worker, filter pipeline, and downstream consumers communicate via interfaces that could be bridged by lightweight gRPC or message queues.
   - For Dockerized deployments:
     - Phase 1: single container hosting capture + stacker + filters.
     - Phase 2: optional split into dedicated containers (capture producer, stacker/filter processor, consumer fan-out) to support horizontal scaling or external GPU-accelerated processing.
   - Document resource requirements per container (CPU, memory, I/O) to aid scheduling in future Kubernetes or Docker Swarm environments.

## Open Questions

- Should we retain a synchronous stacking mode for very low-power hardware? (Possible feature flag.) - YES
- Do we want to expose queue depth metrics via Prometheus/health endpoints immediately or defer until we evaluate the initial implementation?
- How aggressively should we drop frames when using the drop policy—only when the queue is at capacity or proactively when the stacker time exceeds the capture interval?
- What compression algorithms offer the best balance between throughput and memory savings for the buffered frames? (Zstd vs Deflate vs in-memory delta encoding?)
- Which container split provides the best cost/performance trade-off for remote deployments (single node vs multi-node)?

## Next Steps

- Review this plan, adjust for any missing requirements (e.g., timelapse integration specifics).
- Once confirmed, begin implementation on `feature/background-stacker` following the staged plan above.
