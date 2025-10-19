# SkyMonitor v5 UX / UI Overhaul Plan

## Overview
This plan captures the scoped tasks for the SkyMonitor v5 user experience refresh and the supporting theme updates in the shared `hvo-dark` assets.

Recent work introduced an API-first media delivery path so the monitor and frame detail views stream imagery via the Local API with buffer fallbacks.

## Objectives
- Separate the current dashboard experience into a future-ready landing page and a dedicated monitor view.
- Deliver consistent navigation patterns using badge-style menu buttons and diagnostics-inspired tab rows.
- Improve operator workflows in Configuration through scoped controls, collapsible groupings, and camera adapter lifecycle management.
- Modernize Diagnostics with clearer telemetry groupings and a real-time log viewer.
- Provide documented theme primitives so all HVO web properties can adopt the new patterns.

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
- [ ] Integrate the component across Monitor, Image History, Configuration, and Diagnostics with their unique tab sets.
- [ ] Map configuration areas to individual tabs (System, Rig, Cameras, Optics, Pipeline, Filters, etc.).
- [ ] Introduce collapsible sections within dense cards to improve scanability.
- [ ] Provide per-tab Save / Reload / Cancel controls, wiring commands to existing services.

### 4. Configuration Enhancements
- [ ] Surface camera adapter lifecycle actions (Start, Stop, Pause, Reload) within the System tab.
- [ ] Audit configuration forms for Result<T> usage, validation, and logging alignment.
- [ ] Update documentation/runbooks to reflect new configuration flows.

### 5. Diagnostics Modernization
- [ ] Reorganize telemetry into grouped tabs, highlighting newly captured metrics.
- [ ] Embed a real-time log viewer tab with throttled polling aligned to the active view.
- [ ] Rationalize existing diagnostics polling intervals to avoid duplicate requests.
- [ ] Add export/snapshot controls for offline diagnostics review.

### 6. Validation & Release Readiness
- [ ] Exercise the updated UI against physical hardware when available (capture notes on exposure behavior and queue pressure).
- [ ] Re-run stress harnesses or simulations to confirm UI-driven controls do not regress pipeline throughput.
- [ ] Coordinate milestone tagging and release notes once documentation and UI updates are complete.

### 7. Media Streaming & Performance
- [x] Introduce the `FrameMediaProvider` with API-first retrieval and frame-buffer fallback.
- [x] Switch processed and raw detail pages to use the media provider and async loading flows.
- [x] Update the Monitor page to reuse streamed media for the live tiles.
- [ ] Add unit coverage validating provider caching, API fallback, and native/raw descriptor handling.
- [ ] Support format selection via the `type` query parameter on detail routes.
- [ ] Surface quick-download actions on the Monitor cards using cached media URIs.

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
1. Align with stakeholders on milestone sequencing and resource availability.
2. Prototype the badge navigation using theme tokens to validate spacing and accessibility.
3. Finish Workstream 7 follow-on items: add provider test coverage, honor detail format selection, and wire monitor download actions.
4. Start implementation under dedicated branches per workstream, gating merges on UI reviews.
