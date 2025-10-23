# HVOv9 Master TODO

> **Document Status**: Master task tracker for the entire HVOv9 workspace  
> **Last Updated**: 2025-10-22  
> **Task Status Legend**:
> - `[ ]` - Pending/Not Started
> - `[x]` - Completed
> - `[~]` - In Progress
> - `[DEFERRED]` - Deferred to future release
> - `[REMOVED]` - Removed/Not Applicable

---

## Documentation index (quick links)

- Guides (cross-cutting): `docs/guides/`
	- Dev container env: `docs/guides/devcontainer-environment-setup.md`
	- Dev container extensions: `docs/guides/dev-container-extensions.md`
	- Copilot/tools strategy: `docs/guides/github-copilot-tools-setup.md`
	- Blazor best practices: `docs/guides/blazor-component-best-practices.md`
- Projects:
	- RoofController V4 (RPi): `docs/projects/roof-controller-v4-rpi/`
		- Quick start: `docker-quick-start.md`
		- Reference: `docker-reference.md`
		- API, hardware, logging, ops: see folder docs
	- SkyMonitor V5: `docs/projects/sky-monitor-v5/`
		- Deployment: `docker-deployment-guide.md`
		- Operations: `skymonitor-v5-operations-runbook.md`
		- Migration: `skymonitor-v5-json-migration-guide.md`
		- Design notes: `rig-catalog-design.md`, `background-stacker-design.md`, `skia-sharp-pipeline-notes.md`, `sky-monitor-starfield.md`
	- NINA client: `docs/projects/nina-client/`
		- Resilience architecture: `resilience-architecture.md`
		- Profile API usage: `profile-api-usage.md`
	- IoT devices: `docs/projects/iot-devices/gpio-di-setup.md`
	- Website Playground: `docs/projects/website-playground/weather-api-guide.md`
- Testing: `docs/testing/mstest-standardization.md`
- Benchmarks overview: `docs/performance-benchmarks.md`
 - Scripts index: `scripts/README.md`

---

## Solution-Wide

### Ongoing Maintenance
> These are continuous improvement areas without specific completion dates:
- Periodic NuGet package updates and dependency management
- Code quality improvements and refactoring as needed
- Cross-project pattern standardization as new patterns emerge

### Recent Completions (October 2025) ✅
- [x] Fix all 97 MSTest analyzer warnings across 21 test files
- [x] Update .NET SDK from 9.0.302 to 9.0.304
- [x] Update Microsoft.CodeAnalysis.Analyzers from 3.3.4 to 4.14.0
- [x] Migrate NamedOneOfGenerator to IIncrementalGenerator API
- [x] Fix bash unbound variable errors in devcontainer (POSH_SESSION_ID, STARSHIP_SESSION_KEY, Python guards)
- [x] Localize strict mode in hvo-env.sh to prevent leaking into interactive shells

### Completed Infrastructure ✅
- [x] Standardize EF Core dependencies to 9.0.10 across solution
- [x] Deploy `dotnet-ef` global tool for migration support
- [x] Establish workspace-wide Result<T> pattern for error handling
- [x] Establish MSTest standardization patterns across test suite
- [x] Implement structured logging with ILogger<T> throughout workspace

---

## HVO.SkyMonitorV5.RPi

### Camera & Hardware
#### Active Tasks
- [ ] Perform full end-to-end validation against physical camera hardware once access is restored
- [ ] Capture notes on exposure behavior, queue pressure, and on-device telemetry before release
- [ ] Exercise ZWO ASI174MM/ASI174MC with real hardware to verify zero-copy capture paths
- [ ] Validate RAW16 down-conversion and RGB24 Bayer colour ordering on hardware
- [ ] Measure end-to-end latency and dropped-frame behavior with real exposures (1-60s)
- [ ] Exercise gain/exposure auto modes to ensure control caps align with rig configuration
- [ ] Verify cooler/temperature telemetry for cooled camera bodies
- [ ] Stress test ROI/binning reconfiguration for sensor windowing/downscaling

#### Completed Tasks ✅
- [x] Extend `CameraSpec`/`RigSpec` metadata with capability flags (Color, Monochrome, Cooled, DSLR, CMOS, CCD)
- [x] Unify synthetic and physical camera adapters with `Synthetic` flag control
- [x] Break camera adapter workflow into explicit pipeline stages
- [x] Implement zero-copy capture via `SKPixmap` + `SKImage` wrappers
- [x] Introduce `FrameContext` with rig/projector/engine metadata
- [x] Implement camera driver attribute-based discovery system
- [x] Remove adapter catalog layer in favor of driver metadata

### Frame Processing & Pipeline
#### Active Tasks
- [ ] Re-run stress harnesses to validate export channel capacity/backpressure defaults
- [ ] Add unit tests covering `FrameMediaProvider` caching behavior and API fallback
- [ ] Benchmark GPU-backed surfaces vs CPU-only for overlays (if applicable)
- [ ] Evaluate memory pressure of storing high-bit `SKImage` masters in queues at target frame rates
- [DEFERRED] Re-evaluate partial parallel filter execution after profiling shows ~10ms gain matters

#### Completed Tasks ✅
- [x] Implement `RollingFrameStacker` with linear `SKSurface` pooling
- [x] Introduce `SkiaSurfacePool` for reusable linear RgbaF16 surfaces
- [x] Migrate all filters to `IImageFrameFilter` with pooled surface support
- [x] Implement overlay asset caching via `SKPicture` and pre-rasterized `SKImage`
- [x] Complete SkiaSharp pipeline transition (Phases 1-7)
- [x] Establish deterministic composition on linear surfaces
- [x] Implement background stacker with adaptive queue capacity

### Frame Export & Remote Dispatch
#### Active Tasks
- [ ] Expose `FrameExportOptions` in admin configuration UI
- [ ] Provide CLI/support tooling (`scripts/export-frame-diagnostics.sh`) for export diagnostics
- [ ] Document frame export operational runbook (S3 prefixes, retention, troubleshooting)
- [ ] Perform TODO sweep on exporter/resilience code paths for standards compliance
- [ ] Support processed/raw download format selection via `type` query parameter
- [ ] Surface quick-download shortcuts on Monitor cards using cached media URIs
- [ ] Tag release milestone for frame export project with ops/support review notes
- [DEFERRED] FITS/TIFF encoder support for remote dispatch (pending data store completion)

#### Completed Tasks ✅
- [x] Implement `FrameExportPublisher` with bounded channel dispatcher
- [x] Create `S3FrameExportSink` and `FilesystemFrameExportSink`
- [x] Implement frame export telemetry and retry queue
- [x] Integrate MinIO client with bucket auto-provisioning
- [x] Implement raw frame dispatch through MinIO with configurable formats
- [x] Add remote dispatch telemetry to dashboard and diagnostics
- [x] Complete processed frame export with dual-scope (archive/delivery) support
- [x] Implement encoder integration with payload metadata

### Image History & Archive
#### Active Tasks
- [ ] Add background job to backfill archive thumbnails for legacy processed frames

#### Completed Tasks ✅
- [x] Implement `ImageFrameArchiveContext` with EF Core migration
- [x] Create `ImageFrameArchiveIngestionService` with thumbnail generation
- [x] Expand `FrameMediaProvider` to include archive lookup tier
- [x] Build Image History API endpoints and DTOs
- [x] Implement Image History Blazor UI with filters and pagination
- [x] Complete keyboard navigation and progressive loading for thumbnail rail
- [x] Adopt `SKSamplingOptions` to resolve SkiaSharp deprecation warnings

### Data Store & Configuration
#### Active Tasks
- [ ] Add diagnostics overlay entity/table to configuration store
- [ ] Model retention policies in telemetry/configuration store

#### Completed Tasks ✅
- [x] Finalize configuration UX for editing stored rigs/cameras/optics
- [x] Create `HVO.SkyMonitorV5.Data` project with EF Core contexts
- [x] Migrate catalog contexts (HYG, Constellation, Deep Sky) to data project
- [x] Implement `SkyMonitorConfigurationContext` with seed defaults
- [x] Create telemetry database schema with retention helpers
- [x] Implement `SkyMonitorTelemetryRecorder` and ingestion service
- [x] Complete Phase 4: Observability & Operations deliverables
- [x] Publish operations runbook and JSON migration guide
- [x] Implement diagnostics endpoint for data store metrics
- [x] Archive legacy `appsettings.json` content

### UI/UX Improvements
#### Active Tasks
- [ ] Add light-mode theme variant with runtime toggle
- [ ] Split pipeline information into two-column layout on dashboard
- [ ] Surface end-to-end frame processing time metrics
- [ ] Capture diagnostics snapshots to disk (JSON export)
- [ ] Factor remote dispatch configuration editor into reusable component
- [DEFERRED] Promote current landing page to dedicated Monitor view (pending validation)
- [DEFERRED] Refresh top-level navigation with badge-styled buttons (pending validation)

#### Completed Tasks ✅
- [x] Display observatory-local time in footer
- [x] Add diagnostics navigation tabs (queue, filter, system)
- [x] Enable auto-refresh with per-tab throttling
- [x] Implement camera adapter lifecycle controls (Start, Stop, Pause, Reload)
- [x] Implement 4-row diagnostics layout with action buttons
- [x] Add real-time log viewer with 2-second refresh
- [x] Optimize polling intervals per diagnostics tab
- [x] Add JSON export controls across all diagnostic tabs
- [x] Introduce collapsible sections within configuration cards
- [x] Implement secondary tab navigation component

### Documentation & Diagrams
#### Active Tasks
- [ ] Regenerate `skymonitor-flow.svg` and `skymonitor-sequence.svg` with updated architecture
- [ ] Audit design diagrams in docs and rebuild outdated folder structure illustrations
- [ ] Migrate CelestialAnnotations filter configuration documentation

#### Completed Tasks ✅
- [x] Document frame context & rig integration architecture
- [x] Publish SkiaSharp pipeline design notes and transition plan
- [x] Create camera driver migration guide
- [x] Document SkyMonitor V5 operations runbook
- [x] Publish JSON configuration migration guide

---

## HVO.SkyMonitorV5.RPi.Stress

### Active Tasks
- [ ] Expand stress harness scenarios for hardware validation
- [ ] Document stress testing procedures and baselines

### Completed Tasks ✅
- [x] Implement stress harness with duration/sample parameters
- [x] Add automatic workspace data root injection
- [x] Apply configuration/telemetry migrations before scenarios
- [x] Complete 60-second stress validation runs

---

## HVO.SkyMonitorV5.RPi.Tests

### Active Tasks
- _No open items tracked._

### Completed Tasks ✅
- [x] Establish MSTest standardization across test suite
- [x] Add service mocking for integration tests
- [x] Create enhanced TestWebApplicationFactory
- [x] Suppress CS1030 warnings for clean builds
- [x] Add FrameFilterPipeline deterministic output tests
- [x] Implement FramePreprocessingOrchestrator coverage
- [x] Add comprehensive filter regression tests
- [x] Complete Image History service and controller tests

---

## HVO.WebSite.v9

### Active Tasks
- _No open items tracked._

### Completed Tasks ✅
- [x] Establish base website structure
- [x] Integrate shared HVO themes

---

## HVO.RoofControllerV4.RPi

### Active Tasks
- _No open items tracked._

### Completed Tasks ✅
- [x] Complete Docker deployment guide
- [x] Publish hardware overview documentation
- [x] Create API reference documentation
- [x] Establish logging reference
- [x] Publish operator cheat sheet
- [x] Complete troubleshooting guide

---

## HVO.WebSite.Themes

### Active Tasks
- [ ] Extend `hvo-dark.css` with shared badge-style navigation tokens
- [ ] Document recommended markup patterns for nav badges and tab rows

### Completed Tasks ✅
- [x] Create base HVO Dark theme
- [x] Establish CSS custom properties for theme values
- [x] Implement theme utilities for dark backgrounds

---

## HVO.NinaClient

### Active Tasks
- _No open items tracked._

### Completed Tasks ✅
- [x] Implement Result<T> pattern throughout client
- [x] Create comprehensive resilience architecture (retry + circuit breaker)
- [x] Document profile API usage patterns
- [x] Establish NINA integration best practices

---

## HVO.Iot.Devices

### Active Tasks
- _No open items tracked._

### Completed Tasks ✅
- [x] Implement GPIO dependency injection setup
- [x] Create MemoryGpioControllerClient simulator
- [x] Document DI-based testing patterns
- [x] Establish hardware/mock switching patterns

---

## Future Projects & Initiatives

### .NET 10 Migration Readiness
#### Planned Tasks
- [ ] Introduce feature-gated SIMD paths for exposure analyzer
- [ ] Add partial methods for hardware-accelerated pixel conversions
- [ ] Document extension points for .NET 10 math helpers
- [ ] Capture baseline metrics on Raspberry Pi 5 and x64 platforms
- [ ] Add CI hooks for new benchmarks
- [ ] Confirm target .NET 10 variant (LTS vs STS)
- [ ] Evaluate GPU acceleration complementing CPU intrinsics

#### Completed Prep Work ✅
- [x] Introduce `ProjectionVector` struct with static-abstract math
- [x] Extract `ExposureAccumulator` with span-based baseline
- [x] Refactor pixel conversions to operate on `Span<byte>`/`Span<ushort>`
- [x] Define `INativeBufferLease` interface for buffer abstraction
- [x] Centralize allocation in factory for future native memory pool
- [x] Introduce calibration pipeline interface for preprocessing
- [x] Extend BenchmarkDotNet harnesses for projection and conversions

### SkiaSharp Pipeline Future Work
- [ ] Benchmark GPU-backed surfaces vs CPU-only overlays
- [ ] Confirm third-party consumers can ingest SKImage-based outputs
- [ ] Evaluate overlay asset memory usage and eviction policies
- [ ] Add instrumentation for surface pool hit rates

### Data Store Future Enhancements
- [ ] Implement configuration versioning/audit metadata
- [ ] Add UI for inspecting telemetry tables
- [ ] Evaluate catalog DB versioning mechanism
- [ ] Expand retention policy configurability
- [ ] Plan multi-station replication architecture
- [ ] Design cloud sync strategy

### General Future Work
- [ ] Revisit FITS/TIFF encoder support for scientific formats
- [ ] Evaluate multi-tenant dispatch targets (multiple buckets/exchanges)
- [ ] Design serialization format for S3 payload downstream consumers
- [ ] Consider per-frame detail events for background stacker observability
- [ ] Plan admin UI for editing configuration stored in database
- [ ] Design frame history for raw exposure archive entries

---

## Completed Major Projects 🎉

### Phase 3.3 - Remote Dispatch & Background Stacker ✅
**Status**: Complete (2025-10-12)
- Remote frame dispatch through MinIO with configurable formats
- Background stacker with adaptive queue capacity
- Comprehensive telemetry and diagnostics integration
- Stress testing validation complete

### Frame Context & Rig Integration ✅  
**Status**: Complete (2025-10-11)
- `FrameContext` record with rig/projector/engine metadata
- Context-aware filter pipeline
- Adaptive capture pacing with queue pressure response
- Performance benchmark suite established

### SkyMonitor V5 Data Store Project ✅
**Status**: Phase 4 Complete (2025-10-12)
- `HVO.SkyMonitorV5.Data` project with EF Core infrastructure
- Configuration and telemetry contexts with migrations
- Catalog integration (HYG, Constellation, Deep Sky)
- Operations runbook and migration guides published
- Diagnostics instrumentation complete

### SkiaSharp Pipeline Transition ✅
**Status**: Phase 7 Complete (2025-10-16)
- Zero-copy capture with SKPixmap + SKImage
- Linear surface pooling for preprocessing and filters
- Overlay asset caching (SKPicture + pre-rasterized SKImage)
- Deterministic composition and encoding
- Comprehensive test coverage and benchmarks

### Frame Export Project ✅
**Status**: Core Delivery Complete (2025-10-14)
- Unified export pipeline with S3 and filesystem sinks
- Dual-scope (archive/delivery) export support
- Telemetry, retry queue, and resilience policies
- MinIO integration with auto-provisioning

### Camera Driver Refactor ✅
**Status**: Complete (2025-10-XX)
- Attribute-driven driver discovery system
- Removed adapter catalog layer
- Strongly-typed driver configuration support
- Runtime driver registry with validation

### SkyMonitor V5 UX Overhaul ✅
**Status**: Workstream 6 Complete (2025-10-21), Plan Paused
- Theme foundation with badge navigation tokens
- Navigation restructure with Monitor page
- Secondary tab component across all views
- Configuration lifecycle controls
- Diagnostics modernization with real-time log viewer
- Simulator and stress validation complete

### Image History (Workstream 7) ✅
**Status**: Complete (2025-10-22)
- Archive store with EF Core integration
- Processed frame ingestion pipeline with thumbnails
- Image History API and Blazor UI
- Keyboard navigation and progressive loading
- SkiaSharp deprecation warnings resolved

---

## Notes & Follow-Ups

### Hardware Validation Pending
Several items await physical Raspberry Pi 5 hardware access:
- ZWO camera adapter end-to-end validation
- Long-duration stress testing
- Exposure analysis behavior verification
- Queue pressure tuning under real workloads

### Documentation Maintenance
- Keep `TODO.md` synchronized with completed work
- Archive completed project plans to preserve history
- Update architecture diagrams as major changes land
- Maintain operations runbooks for deployment guidance

### Code Quality Standards
- Continue Result<T> pattern adoption across services
- Maintain structured logging with ILogger<T>
- Ensure automatic model validation in API controllers
- Keep disposal patterns consistent in pipeline stages
- Follow MSTest AAA pattern in test suites

---

**Document Consolidation Note**: This master TODO consolidates and replaces previous project-specific planning documents, many of which have been completed and archived. 

**Active Project Documentation**:
- **SkyMonitor V5**: See `docs/projects/sky-monitor-v5/` for deployment guide, operations runbook, and migration guides
- **RoofController V4**: See `docs/projects/roof-controller-v4-rpi/` for docker deployment guide
- **.NET 10 Readiness**: See `docs/projects/dotnet10-readiness-plan.md` for upgrade planning

**Completed Project Archives**: Historical project planning documents can be found in `docs/archive/completed-projects/` for reference.
