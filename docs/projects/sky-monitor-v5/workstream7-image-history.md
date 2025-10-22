# Workstream 7 — Image History (Design Sub-Plan)

Status: In Progress (archive store + ingestion queue complete)

## Progress (2025-10-21)
- [x] Task 1 — Author design sub-plan and review scope
- [x] Task 2 — Introduce archive EF model/context, DI registration, migration (`ImageFrameArchiveContext`)
- [x] Task 3 — Implement processed-frame archive ingestion pipeline (`ImageFrameArchiveIngestionService`, queue wiring, thumbnails, options)
- [x] Task 4 — Expand `FrameMediaProvider` lookup tiers to read from archive
- [x] Task 5 — Expose Image History API endpoints and DTOs
- [ ] Task 6 — Build Blazor Image History UI and integrate telemetry/tests

## Goal
Provide a focused, performant "Image History" experience for operators to review recent composed (stacked) processed frames and drill into raw exposures on demand. Reuse existing media export and pre-processing sinks while adding a small archive catalog and provider changes so Monitor and History pages share a single frame lookup path.

## Contract
- Inputs: time-range (since/until), rig/camera filters, paging/cursor, optional pipeline status filters
- Outputs: paged list of archived composed frames with thumbnail URIs, per-frame metadata (exposure, stack count, applied filters, queue latencies), and media retrieval endpoints (processed, raw, thumbnail)
- Error modes: catalog DB unavailable, missing media payloads, S3 errors; report `Result<T>.Failure()` on service/API surfaces.
- Success criteria: history page lists composed frames for a selected range with thumbnails loading progressively; Monitor pre-populates latest archived processed frame on render.

## Scope decisions
- History covers only composed/stacked processed frames for v1. Raw exposures are persisted by the pre-processing sink and may be re-rendered on-demand using stored metadata.
- Thumbnails: 320-pixel max-axis JPEG at quality 86 for browsing; full-resolution processed payloads remain available for download.
- Archive store: EF Core-backed lightweight `FrameArchive` table stored by default in `datastores/telemetry/image_frame_archive.sqlite`. S3 manifests may be used when remote sinks are enabled.

## Data Model (EF Core)
```csharp
public sealed record FrameArchive
{
    public Guid FrameId { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public string RigName { get; init; } = string.Empty;
    public string CameraName { get; init; } = string.Empty;
    public int FramesStacked { get; init; }
    public int? IntegrationMilliseconds { get; init; }
    public string[] AppliedFilters { get; init; } = Array.Empty<string>();
    public double? QueueLatencyMilliseconds { get; init; }
    public string PayloadContentType { get; init; } = "image/jpeg";
    public string PayloadExtension { get; init; } = "jpg";
    public string? ThumbnailFilePath { get; init; }
    public string? ThumbnailObjectKey { get; init; }
    public string? MediaFilePath { get; init; }
    public string? MediaObjectKey { get; init; }
    public string? RawMediaFilePath { get; init; }
    public string? RawMediaObjectKey { get; init; }
}
```

Indexes: CapturedAtUtc, RigName, CameraName, FramesStacked

## Ingestion flow
1. After composition completes, ProcessedFrame pipeline publishes an archive job (non-blocking) to a background channel.
2. Archive worker (`ImageFrameArchiveIngestionService`) creates thumbnail using `Skia` helpers, writes thumbnail/payload manifest to each configured sink (file system, S3) and captures both references in the archive record.
3. Worker emits telemetry metrics (archive latency, thumbnail generation ms, upload bytes) and records transport failures when a sink is temporarily unavailable.
4. On failure, retry with exponential backoff; surface persistent failures to the existing FrameExportRetryService for manual replay. Network outages must not block the capture pipeline—archive jobs fall back to the sinks that succeeded and keep retrying the rest. (Current implementation logs and drops to retry queue when sinks reject uploads.)

## FrameMediaProvider changes
- Upgrade provider lookup tiers to: in-memory `IFrameStateStore` (latest buffer) -> `FrameArchive` DB (by frame id or latest) -> local FS/S3 (manifest) -> local API buffer (fallback).
- Implemented archive tier for processed frames (monitor falls back to archive when live frame is unavailable).
- Expose `GetLatestProcessedFrameAsync()` variant that optionally queries archive first and returns the best available `FrameMedia`.
- Cache thumbnail URIs and small payloads in `IMemoryCache` with conservative size and sliding TTL.
- Emit structured logs and counters when falling back between tiers, and degrade gracefully when file system or S3 endpoints are unreachable (log warning, skip to next tier, keep retry queue informed).

## API
`ImageHistoryController` (/api/v1.0/history)
- GET /thumbnails?since=&until=&rig=&camera=&pageSize=&cursor=
- GET /frames/{frameId}
- GET /frames/{frameId}/media?variant=processed|raw|thumbnail
- GET /stats?since=&until=

All endpoints return `Result<T>` semantics and `ProblemDetails`-friendly HTTP responses.
Implementation landed in `ImageHistoryController` + `ImageHistoryService` with MSTest coverage (`ImageHistoryServiceTests`) ensuring paging, fault handling, and aggregate stats.
> Build note (2025-10-21): `ImageFrameArchiveIngestionService` still emits SkiaSharp deprecation warnings (`SKFilterQuality`/`SKBitmap.Resize`). Track follow-up to adopt `SKSamplingOptions` when thumbnail generation work resumes.

## Blazor UI components
- `ImageHistoryFilters.razor` — date range picker, quick presets, rig/camera chips, pagination control.
- `ImageHistoryRail.razor` — horizontal virtualized thumbnail rail grouped by hour/day with lazy fetch and keyboard accessibility.
- `ImageHistoryDetail.razor` — detail panel with large image preview, metadata grid, small `HistoryLineChart` for queue/exposure trends, and actions (download processed/raw, regenerate overlay).

UX Details
- On page load, rail pre-fetches the most recent 100 thumbnails (batched by day/hour). Selecting a thumbnail loads detail panel.
- Keyboard: left/right to move, Enter to open detail, Space to toggle play (auto-advance recent frames).
- Mobile: stacked layout — filters collapsible, rail becomes vertical list.

## Tests
- Unit tests: provider tier lookup, thumbnail builder, archive worker retry logic.
- Integration test: `ImageHistoryController` with in-memory DB and mocked storage client returning sample payloads.
- UI smoke: Blazor component rendering tests for rail and detail (shallow).

## Acceptance criteria
- Monitor pre-populates with the latest archived processed frame on render.
- History page lists composed frames by time with thumbnails and allows drill-down to metadata and media download.
- Provider correctly falls back through lookup tiers and logs tier transitions.

## Migration & rollout
- Add migration to create `FrameArchive` table. Default DB path: `datastores/telemetry/image_frame_archive.sqlite`.
- Feature flag `ImageHistory:EnableArchive` (implemented via `ImageHistoryOptions`) gates data writes during early rollout.

## Roll-forward items
- Add optional per-exposure archive entries for raw frames if operators request it.
- Background job to backfill thumbnails for existing exported frames in `artifacts/exports/`.

## Open Questions
- Retention policy: configurable via appsettings (`ImageHistory:RetentionDays`) with 90-day default, and surfaced through the existing system configuration/telemetry overrides so operations can tune it without redeploying.
- Thumbnail storage: FS by default, S3 optional. Need to confirm bucket layout and lifecycle rules.

---

Author: Draft by engineering pairing
Date: 2025-10-21
