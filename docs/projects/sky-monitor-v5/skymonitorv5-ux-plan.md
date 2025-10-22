# SkyMonitor v5 UX / UI Overhaul Plan

## Overview
This plan captures the scoped tasks for the SkyMonitor v5 user experience refresh and the supporting theme updates in the shared `hvo-dark` assets.

Recent work introduced an API-first media delivery path so the monitor and frame detail views stream imagery via the Local API with buffer fallbacks.

> **Status (Oct 22, 2025):** Primary goals delivered; plan paused with remaining follow-ups tracked in `docs/TODO.md`. Resume this plan after validation milestones complete.

## Objectives
- Separate the current dashboard experience into a future-ready landing page and a dedicated monitor view.
- Deliver consistent navigation patterns using badge-style menu buttons and diagnostics-inspired tab rows.
- Improve operator workflows in Configuration through scoped controls, collapsible groupings, and camera adapter lifecycle management.
- Modernize Diagnostics with clearer telemetry groupings and a real-time log viewer.
- Provide documented theme primitives so all HVO web properties can adopt the new patterns.

## Current Priority
- **✅ Workstreams 3, 4, and 5 COMPLETED** (Oct 21, 2025)
- **✅ Workstream 7 (Image History UX) COMPLETED** (Oct 22, 2025)
- **🎯 Active Focus: Workstream 6 — Validation & Release Readiness**
- Plan is paused; monitor validation progress and revisit once Workstream 6 closes out. Outstanding enhancements live in `docs/TODO.md`.

## Workstreams & Tasks

### 1. Theme Foundation (`HVO.WebSite.Themes`)
- [x] Introduce badge-style navigation tokens and tab styling primitives in `hvo-dark.css`.
- [x] Add theme documentation covering markup and utility classes for badges and secondary tab rows.
- [x] Validate contrast and spacing across SkyMonitor, Roof Controller, and v9 sites after theme updates (completed during October 2025 preview rollout).

### 2. Navigation Restructure (`HVO.SkyMonitorV5.RPi`)
- [x] Move the existing homepage content to a new `Monitor` page/component.
- [x] Leave the root `Dashboard` route as a lightweight placeholder pending future content.
- [x] Update the primary navigation to use badge buttons for: Dashboard, Monitor, Image History, Configuration, Diagnostics.
- [x] Ensure navigation consumes the shared theme tokens to maintain visual parity.
- [x] Make the primary navigation bar full-width (matching the footer) with menu items left-aligned and user account/login controls right-aligned.

### 3. Secondary Tabs & Layout Hygiene
- [x] Introduce reusable secondary tab component with page-specific definitions.
- [x] Re-baseline the tab map after the camera driver refactor, ensuring dedicated tabs for System, Rig, Drivers, Cameras, Optics, Pipeline, Filters, and new adapter diagnostics.
- [x] Integrate the component across Monitor, Image History, Configuration, and Diagnostics with their updated tab sets.
- [x] Introduce collapsible sections within dense cards to improve scanability (System, Rig, Driver catalog, and Adapter diagnostics cards now share the pattern).
- [x] Confirm driver and adapter tabs remain informational with refresh-only controls, relying on per-section save/cancel actions where editing is supported (global save/cancel removed).

### 4. Configuration Enhancements ✅ **COMPLETED** (Oct 21, 2025)
- [x] Surface camera adapter lifecycle actions (Start, Stop, Pause, Reload) within the System tab.
- [x] Audit configuration forms for Result<T> usage, validation, and logging alignment.
- [x] Update documentation/runbooks to reflect new configuration flows.

**Completion Notes:**
- Camera adapter lifecycle controls implemented in `DriverLifecycleControls` component within Drivers tab
- Result<T> patterns extensively implemented across configuration services (`SystemConfigurationService`, etc.)
- Validation patterns using `EditContext`, `ValidationSummary`, and `DataAnnotationsValidator` consistent across all forms
- Structured logging with `ILogger<T>` implemented throughout configuration services and hardware device classes

### 5. Diagnostics Modernization ✅ **COMPLETED** (Oct 21, 2025)
- [x] Reorganize telemetry into grouped tabs, highlighting newly captured metrics.
- [x] Embed a real-time log viewer tab with throttled polling aligned to the active view.
- [x] Rationalize existing diagnostics polling intervals to avoid duplicate requests.
- [x] Add export/snapshot controls for offline diagnostics review.

**Completion Notes:**
- 4-row diagnostics layout implemented with action buttons, system status, CPU/memory cards, and capture snapshot
- Driver tab integration with runtime telemetry and lifecycle controls
- CPU core metrics with responsive flex-wrap layout (1-3 columns)
- Real-time log viewer with 2-second refresh in Logs tab, pagination, and severity display
- Optimized polling intervals per tab: 2s (Logs), 3s (Pipeline), 5s (Overview/System/Dispatch), 7s (Exports), 30s (Storage)
- Download JSON export controls across all diagnostic tabs (system, metrics, history, storage)

### 6. Validation & Release Readiness
- [x] **🎯 SIMULATOR TESTING**: Exercise the updated UI against simulators to validate UI-driven controls and capture behavior
- [x] **🎯 SIMULATOR TESTING**: Re-run stress harnesses and simulations to confirm UI-driven controls do not regress pipeline throughput
- [x] **📋 DOCUMENTATION**: Milestone tagging and release notes drafted in this doc; ready for PR
- [ ] **⏸️ POSTPONED**: Exercise against physical hardware (requires actual hardware availability) - capture notes on exposure behavior and queue pressure

**Validation Notes (2025-10-21):**
- Simulator host (`dotnet run --configuration Debug`) captured healthy diagnostics via `api/v1.0/diagnostics/system` (total CPU ≈ 8.5%, process CPU ≈ 6.7%, ample memory headroom).
- Background stacker metrics show queue depth ≤ 2/24 (≈8% peak fill), average stacking ≈ 58 ms, confirming pacing controls behave as expected in mock environment.
- All-sky status endpoint confirms continuous frame production (stacking count 4, integration 4 s) with overlays, aperture mask, and adapter telemetry active.
- Ready to launch UI session against simulator or capture screenshots on next pass; stress harness runs remain pending.
- Stress harness matrix (`dotnet run --project HVO.SkyMonitorV5.RPi.Stress -- --duration-seconds 60 --sample 5`) now injects the workspace data root automatically and applies configuration/telemetry migrations before each scenario. Runs complete without EF migration warnings, queue depth remains ≤ 1, CPU averages 2–4%, and the latest summary is stored at `artifacts/stress/20251021_173328_stress-summary.json`. Rejection penalties still surface on longer queues (expected), and capture cancellation logs appear when scenarios stop the mock camera.
- Cancellation noise cleanup is in progress: capture, telemetry, and background services now downgrade requested shutdowns to debug/trace logs, a new unit test covers the helper, and first-chance exception tracking drops to debug/trace severity.

### Workstream 6 — Closure Notes & Release Draft

- Status: Completed (simulator validation, stress harness, logging cleanup, and unit test coverage) — pending physical hardware verification.
- Release tag suggestion: `v5.0.0-rc1` (release candidate for stakeholder review)
- Release notes (draft):
	- Theme enhancements: badge-style navigation tokens and secondary tab primitives landed.
	- Navigation & tabs: Monitor moved, badge menu and secondary tabs implemented across Monitor, Image History, Configuration, and Diagnostics.
	- Configuration revamp: adapter lifecycle controls, scoped validation, and Result<T> patterns applied across services.
	- Diagnostics modernization: real-time log viewer, grouped telemetry tabs, optimized polling intervals, and JSON export controls.
	- Stability: stress harness runs show stable capture and background processing under simulated load; EF Core migrations applied automatically by the harness; cancellation-related logs downgraded to debug/trace to avoid alarming operators during normal shutdown.

### Hardware Validation Checklist (placeholder)

- Pre-checks:
	- Verify latest image/build deployed to test hardware.
	- Ensure network access for remote dispatch tests (or configure isolated test network).
- Capture stability:
	- Run 1 hour capture session with background stacker enabled; target queue depth <= 10% of capacity.
	- Verify no unhandled exceptions in logs; only debug/trace for shutdown cancellations.
	- Validate exposure analysis suggestions do not oscillate (>3 toggles/hour) under stable lighting.
- Adapter lifecycle:
	- Cycle adapter Start/Stop/Reload 10x and confirm state reported in API and UI.
	- Confirm adapter stop cleans up resources (no leaked file handles, no lingering CPU spikes).
- Performance & telemetry:
	- Run stress harness scenario representative of the highest expected load; verify CPU and memory within acceptable limits and queue spikes fall back within configured retry/backpressure windows.
	- Confirm telemetry ingestion keeps up and retention sweeps complete without errors.
- Sign-off criteria:
	- All checks above completed without critical exceptions and with stakeholder approval recorded.

Notes: The hardware validation checklist is intentionally concise — expand with lab-specific steps when hardware time is available.

### 7. Media Streaming & Performance _(Design & Image History UX complete; follow-ups tracked in TODO)_
- [x] Introduce the `FrameMediaProvider` with API-first retrieval and frame-buffer fallback.
- [x] Switch processed and raw detail pages to use the media provider and async loading flows.
- [x] Update the Monitor page to reuse streamed media for the live tiles.
- [x] Deliver Image History page design + implementation (filters, accessible rail, detail view, paging polish).
- [ ] Add unit coverage validating provider caching, API fallback, and native/raw descriptor handling. _(Moved to TODO backlog)_
- [ ] Support format selection via the `type` query parameter on detail routes. _(Moved to TODO backlog)_
- [ ] Surface quick-download actions on the Monitor cards using cached media URIs. _(Moved to TODO backlog)_

## Milestones
- **M1 – Theme Readiness:** Badge/tab tokens merged, documentation drafted.
- **M2 – Navigation & Tabs:** Monitor relocation, badge menu, and tab rows in place.
- **M3 – Configuration Revamp:** Tab controls, collapsible sections, adapter actions wired.
- **M4 – Diagnostics Refresh:** Metrics regrouped, log viewer deployed.
- **M5 – Validation & Release:** Hardware verification, stress tests, documentation handoff.

## Risks & Dependencies
- Hardware availability for end-to-end validation remains limited; schedule windows early.
- Shared theme changes may impact other sites; coordinate previews before merging.
- Diagnostics log viewer polling must be tuned to avoid excessive server load.

## Next Steps
1. Validate the read-only Driver and Adapter Diagnostics experiences cover required telemetry, and capture follow-up tasks for lifecycle command surfaces separately.
2. Maintain configuration enhancements from Workstream 4; capture any regression bugs as they appear during validation.
3. Execute Workstream 6 validation tasks (simulators, stress harness, hardware when available) and close remaining release notes.
4. Revisit this UX plan once Workstream 6 concludes; refer to `docs/TODO.md` for the queued follow-on items.
