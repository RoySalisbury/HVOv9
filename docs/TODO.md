# TODO Catalog

## Solution-Wide

- _No open items tracked. Reserve for NuGet updates, global cleanup, or cross-project initiatives._

## HVO.SkyMonitorV5.RPi
- [ ] Perform full end-to-end validation against physical camera hardware once access is restored; capture notes on exposure behavior, queue pressure, and on-device telemetry before release.
- [ ] Add a light-mode variant of the theme and expose a runtime toggle.
- [ ] Migrate CelestialAnnotations filter configuration (DeepSkyObjects list, thresholds, labels) entirely into the database catalog and remove the unused legacy appsettings entries.
- [ ] Regenerate `docs/projects/sky-monitor-v5/skymonitor-flow.svg` and `docs/projects/sky-monitor-v5/skymonitor-sequence.svg` with the updated architecture once the new diagrams are drafted.
- [ ] Audit design diagrams in docs and rebuild any outdated folder structure illustrations inside the Markdown guides.
- [ ] Expose `FrameExportOptions` in the admin configuration UI so operators can toggle sinks, prefixes, and manifest settings without editing JSON.
- [ ] Provide CLI/support tooling (e.g., `scripts/export-frame-diagnostics.sh`) to inspect recent export attempts and replay failed envelopes.
- [ ] Document the frame export operational runbook covering S3 prefixes, retention, troubleshooting steps, and retry workflows.
- [ ] Perform a TODO sweep on the exporter/resilience code paths to ensure logging, Result<T> usage, and policy wiring match workspace standards.
- [ ] Re-run stress harnesses to validate export channel capacity/backpressure defaults and capture tuning guidance in docs once hardware access resumes.
- [ ] Tag the release milestone for the frame export project and capture final review notes from ops/support once the remaining docs/UI work lands.

### UX Improvements _(deferred to upcoming UI overhaul project)_
- [ ] Promote the current SkyMonitor landing page into a dedicated **Monitor** view and hold the root **Dashboard** route for a future minimal overview.
- [ ] Refresh top-level navigation to use compact badge-styled buttons (matching diagnostics tabs) with entries for: Dashboard, Monitor, Image History, Configuration, Diagnostics.
- [ ] Ensure non-dashboard pages expose secondary tab bars to group content (e.g., Configuration tabs for System, Rig, Cameras, Optics, Pipeline, Filters, etc.).
- [ ] Provide per-tab Save / Reload / Cancel affordances so operators can safely edit configuration slices, including collections like rigs, cameras, and optics.
- [ ] Introduce collapsible sections within dense cards to keep long forms and telemetry groupings scannable.
- [ ] Add system-level controls (start, stop, pause, reload) for the active camera adapter within the Configuration area.
- [ ] Reorganize Diagnostics layout to highlight the expanded telemetry set and add a real-time log viewer tab using the shared tab navigation pattern.

### Dashboard _(deferred to upcoming UI overhaul project)_
- [x] Display observatory-local time in the SkyMonitor footer instead of UTC.
- [ ] Split the pipeline information into a two-column layout, presenting queue stats side-by-side and moving the filters section beneath the capabilities summary.
- [ ] Surface end-to-end frame processing time (capture to pipeline completion) plus inter-frame delay alongside the existing pipeline duration metric.

### Diagnostics _(deferred to upcoming UI overhaul project)_
- [x] Add navigation affordance (tabs or sidebar) so queue diagnostics, filter diagnostics, and system diagnostics sub-views fit without cluttering the layout. The current theme is fine, but may need to slight modifactions in font size (smaller, like the dashbaord).
- [x] Enable auto-refresh with per-tab throttling to ensure only the visible diagnostics pane polls for data, reducing CPU load.
- [ ] Capture diagnostics snapshots to disk for offline analysis (JSON export triggered from the diagnostics page).
- [ ] Factor the remote dispatch configuration editor into a reusable component consumed by SkyMonitor, Roof Controller, and future observatory apps.

### Camera
- [x] Extend `CameraSpec`/`RigSpec` metadata with capability flags (Color, Monochrome, Cooled, DSLR, CMOS, CCD, etc.) and mirror those attributes in the dashboard camera section alongside pipeline capabilities to guide setup decisions.
- [x] Evaluate unifying synthetic and physical camera adapters behind a single implementation controlled by a `Synthetic` flag, sourcing frames from either live hardware or the starfield engine, with hooks for exposure/contrast/gain adjustments pre-pipeline. Explore whether this can converge further into one `CameraAdapter` class that relies on `RigSpec` for behaviour and delegates device-specific calls to `ICamera` implementations. Would also need to be able to access the running CameraAdaptor from things like the UI and API.  We can confine the applicaiotn to a single running adaptor at a time, but multiple configuraitons available.
- [x] Break the camera adapter workflow into explicit pipeline stages (exposure configuration, image acquisition, pre-processing, post-processing, framebuffer assembly) so overrides remain focused and discoverable.

## HVO.SkyMonitorV5.RPi.Stress

- _No open items tracked._

## HVO.SkyMonitorV5.RPi.Tests

- _No open items tracked._

## HVO.WebSite.v9

- _No open items tracked._

## HVO.RoofControllerV4.RPi

- _No open items tracked._

## HVO.WebSite.Themes
- [ ] Extend `hvo-dark.css` with shared badge-style navigation tokens so SkyMonitor, Roof Controller, and v9 sites can reuse the compact menu buttons.
- [ ] Document recommended markup patterns for the new nav badges and diagnostics-style tab rows so consuming projects keep visuals consistent.

## HVO.NinaClient

- _No open items tracked._

## HVO.Iot.Devices

- _No open items tracked._

## Future Projects

- [ ] Revisit FITS/TIFF encoder support for remote dispatch once the SkyMonitorV5 data store project is underway.
- [ ] Stand up the SkyMonitorV5 data store: consolidate telemetry + configuration into the shared database, seed default rigs/cameras/filters when empty, provide diagnostics log persistence, and host an interim Deep Sky object catalog we control until a long-term source is selected.
- [ ] Re-evaluate partial parallel filter execution (Phase 8) after the current freeze; revisit when profiling shows the ~10ms gain matters for upcoming workloads.
