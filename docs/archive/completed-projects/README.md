# Completed Project Plans Archive

This directory contains project plans and status documents for completed initiatives in the HVOv9 workspace. These documents are preserved for historical reference and to provide context for architectural decisions.

## Archived Documents

### Phase 3.3 - Remote Dispatch & Background Stacker
**File**: `phase3_3-status.md`  
**Completed**: 2025-10-12  
**Summary**: Remote frame dispatch through MinIO with configurable image formats, background stacker with adaptive queue capacity, comprehensive telemetry and diagnostics integration. Stress testing validation completed successfully.

### Frame Context & Rig Integration
**File**: `skymonitor-frame-context-plan.md`  
**Completed**: 2025-10-11  
**Summary**: Introduced `FrameContext` record containing rig/projector/engine metadata, context-aware filter pipeline, adaptive capture pacing with queue pressure response, and established performance benchmark suite.

### SkyMonitor V5 Data Store Project
**File**: `skymonitorv5-data-store-project.md`  
**Completed**: Phase 4 on 2025-10-12  
**Summary**: Created `HVO.SkyMonitorV5.Data` project with EF Core contexts for configuration and telemetry, integrated catalog databases (HYG, Constellation, Deep Sky), published operations runbook and JSON migration guides, completed observability instrumentation.

### SkiaSharp Pipeline Transition
**File**: `skia-sharp-pipeline-plan.md`  
**Completed**: Phase 7 on 2025-10-16  
**Summary**: Implemented zero-copy capture with SKPixmap + SKImage wrappers, linear surface pooling for preprocessing and filters, overlay asset caching (SKPicture + pre-rasterized SKImage), deterministic composition and encoding with comprehensive test coverage and benchmarks.

### Frame Export Project
**File**: `processed-frame-export-plan.md`  
**Completed**: Core delivery on 2025-10-14  
**Summary**: Unified export pipeline with S3 and filesystem sinks, dual-scope (archive/delivery) export support, telemetry with retry queue and resilience policies, MinIO integration with auto-provisioning. Remaining polish items tracked in master TODO.

### Camera Driver Refactor
**File**: `camera-driver-refactor-plan.md`  
**Completed**: 2025-10-XX  
**Summary**: Implemented attribute-driven camera driver discovery system, removed adapter catalog layer in favor of driver metadata, added strongly-typed driver configuration support with runtime driver registry and validation.

### SkyMonitor V5 UX Overhaul
**File**: `skymonitorv5-ux-plan.md`  
**Completed**: Workstream 6 on 2025-10-21, Plan paused  
**Summary**: Theme foundation with badge navigation tokens, navigation restructure with Monitor page separation, secondary tab component across all views, configuration lifecycle controls, diagnostics modernization with real-time log viewer. Simulator and stress validation complete. Additional enhancements deferred to future releases.

### Image History (Workstream 7)
**File**: `workstream7-image-history.md`  
**Completed**: 2025-10-22  
**Summary**: Archive store with EF Core integration, processed frame ingestion pipeline with thumbnail generation, Image History API and Blazor UI with filters and pagination, keyboard navigation and progressive loading, resolved SkiaSharp deprecation warnings.

## Active Project Plans

Active project plans remain in their original locations:

- **Future Projects**: `docs/projects/dotnet10-readiness-plan.md` - .NET 10 migration preparation
- **Technical Notes**: `docs/projects/skia-sharp-pipeline-notes.md` - SkiaSharp implementation details
- **Reference Docs**: Various files in `docs/projects/` subdirectories

## Master TODO

The consolidated master TODO document is maintained at: `docs/TODO.md`

This document tracks all active, pending, deferred, and completed tasks across the entire HVOv9 workspace, organized by project and component.

## Document Maintenance

When a new project is completed:
1. Move the project plan markdown file to this archive directory
2. Update this README with a summary entry
3. Ensure completed items are marked in the master TODO
4. Add git tags for major milestones if appropriate

---

**Last Updated**: 2025-10-22
