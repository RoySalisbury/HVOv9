# TODO Catalog

## Solution-Wide

- _No open items tracked. Reserve for NuGet updates, global cleanup, or cross-project initiatives._

## HVO.SkyMonitorV5.RPi

### Dashboard
- [x] Display observatory-local time in the SkyMonitor footer instead of UTC.
- [ ] Split the pipeline information into a two-column layout, presenting queue stats side-by-side and moving the filters section beneath the capabilities summary.
- [ ] Surface end-to-end frame processing time (capture to pipeline completion) plus inter-frame delay alongside the existing pipeline duration metric.

### Diagnostics
- [ ] Add navigation affordance (tabs or sidebar) so queue diagnostics, filter diagnostics, and system diagnostics sub-views fit without cluttering the layout. The current theme is fine, but may need to slight modifactions in font size (smaller, like the dashbaord).
- [ ] Enable auto-refresh with per-tab throttling to ensure only the visible diagnostics pane polls for data, reducing CPU load.

### Camera
- [ ] Extend `CameraSpec`/`RigSpec` metadata with capability flags (Color, Monochrome, Cooled, DSLR, CMOS, CCD, etc.) and mirror those attributes in the dashboard camera section alongside pipeline capabilities to guide setup decisions.
- [ ] Evaluate unifying synthetic and physical camera adapters behind a single implementation controlled by a `Synthetic` flag, sourcing frames from either live hardware or the starfield engine, with hooks for exposure/contrast/gain adjustments pre-pipeline. Explore whether this can converge further into one `CameraAdapter` class that relies on `RigSpec` for behaviour and delegates device-specific calls to `ICamera` implementations.

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
