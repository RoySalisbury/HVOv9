# TODO Catalog

## Solution-Wide

- _No open items tracked. Reserve for NuGet updates, global cleanup, or cross-project initiatives._

## HVO.SkyMonitorV5.RPi
- [ ] Add a light-mode variant of the theme and expose a runtime toggle.

### UX Improvements
- [ ] Promote the current SkyMonitor landing page into a dedicated **Monitor** view and hold the root **Dashboard** route for a future minimal overview.
- [ ] Refresh top-level navigation to use compact badge-styled buttons (matching diagnostics tabs) with entries for: Dashboard, Monitor, Image History, Configuration, Diagnostics.
- [ ] Ensure non-dashboard pages expose secondary tab bars to group content (e.g., Configuration tabs for System, Rig, Cameras, Optics, Pipeline, Filters, etc.).
- [ ] Provide per-tab Save / Reload / Cancel affordances so operators can safely edit configuration slices, including collections like rigs, cameras, and optics.
- [ ] Introduce collapsible sections within dense cards to keep long forms and telemetry groupings scannable.
- [ ] Add system-level controls (start, stop, pause, reload) for the active camera adapter within the Configuration area.
- [ ] Reorganize Diagnostics layout to highlight the expanded telemetry set and add a real-time log viewer tab using the shared tab navigation pattern.

### Dashboard
- [x] Display observatory-local time in the SkyMonitor footer instead of UTC.
- [ ] Split the pipeline information into a two-column layout, presenting queue stats side-by-side and moving the filters section beneath the capabilities summary.
- [ ] Surface end-to-end frame processing time (capture to pipeline completion) plus inter-frame delay alongside the existing pipeline duration metric.

### Diagnostics
- [x] Add navigation affordance (tabs or sidebar) so queue diagnostics, filter diagnostics, and system diagnostics sub-views fit without cluttering the layout. The current theme is fine, but may need to slight modifactions in font size (smaller, like the dashbaord).
- [x] Enable auto-refresh with per-tab throttling to ensure only the visible diagnostics pane polls for data, reducing CPU load.
- [ ] Capture diagnostics snapshots to disk for offline analysis (JSON export triggered from the diagnostics page).
- [ ] Factor the remote dispatch configuration editor into a reusable component consumed by SkyMonitor, Roof Controller, and future observatory apps.

### Camera
- [x] Extend `CameraSpec`/`RigSpec` metadata with capability flags (Color, Monochrome, Cooled, DSLR, CMOS, CCD, etc.) and mirror those attributes in the dashboard camera section alongside pipeline capabilities to guide setup decisions.
- [x] Evaluate unifying synthetic and physical camera adapters behind a single implementation controlled by a `Synthetic` flag, sourcing frames from either live hardware or the starfield engine, with hooks for exposure/contrast/gain adjustments pre-pipeline. Explore whether this can converge further into one `CameraAdapter` class that relies on `RigSpec` for behaviour and delegates device-specific calls to `ICamera` implementations. Would also need to be able to access the running CameraAdaptor from things like the UI and API.  We can confine the applicaiotn to a single running adaptor at a time, but multiple configuraitons available.

## HVO.SkyMonitorV5.RPi.Stress

- _No open items tracked._

## HVO.SkyMonitorV5.RPi.Tests

- _No open items tracked._

## HVO.WebSite.v9

- _No open items tracked._

## HVO.RoofControllerV4.RPi

- _No open items tracked._

## HVO.NinaClient

- _No open items tracked._

## HVO.Iot.Devices

- _No open items tracked._

## Future Projects

- [ ] Revisit FITS/TIFF encoder support for remote dispatch once the SkyMonitorV5 data store project is underway.
- [ ] Stand up the SkyMonitorV5 data store: consolidate telemetry + configuration into the shared database, seed default rigs/cameras/filters when empty, provide diagnostics log persistence, and host an interim Deep Sky object catalog we control until a long-term source is selected.
