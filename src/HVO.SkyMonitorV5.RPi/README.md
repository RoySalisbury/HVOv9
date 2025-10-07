# HVO SkyMonitor v5 Raspberry Pi Host

HVO SkyMonitor v5 is the Raspberry Pi service that captures synthetic or hardware camera frames, performs rolling integration/stacking, and projects annotated all-sky imagery for the Hualapai Valley Observatory. The service ships with a modular processing pipeline that lets you arrange post-processing filters per camera without touching code.

## System overview

- **Capture loop** – `AllSkyCaptureService` orchestrates exposures, capture, stacking, filtering, and persistence.
- **Camera adapters** – Implement `ICameraAdapter` to talk to real hardware or simulations (for development we ship the `MockFisheyeCameraAdapter`).
- **Exposure controller** – `IExposureController` chooses day/night integration settings.
- **Stacking engine** – `RollingFrameStacker` buffers frames and averages them once enough light has accumulated.
- **Filter pipeline** – `FrameFilterPipeline` can execute a configured sequence of `IFrameFilter` instances (overlays are disabled by default so frames mirror the raw starfield output).
- **State store** – `FrameStateStore` keeps track of the latest raw/processed frames and active configuration for API and UI consumers.

## Frame processing filters

> Filters are optional and not registered in the default deployment. Enable them if you need annotations or masks layered onto the synthetic starfield.

| Filter | Name (for configuration) | Purpose |
| ------ | ------------------------ | ------- |
| Cardinal directions | `CardinalDirections` | Draws a dashed azimuth ring with north/east/south/west tick marks and labels, helping viewers orient the dome.
| Celestial annotations | `CelestialAnnotations` | Projects a curated set of celestial labels (Polaris, Vega, Mars, etc.) in their sidereal-corrected positions with color-coded markers.
| Overlay text | `OverlayText` | Renders timestamp, exposure, gain, and rolling integration summary in the lower-left corner of the frame.
| Circular mask | `CircularMask` | Darkens the corners outside a 95% diameter circle to mimic traditional all-sky fisheye vignetting or privacy masks.

Each filter implements `IFrameFilter`, receives the `SKBitmap` for the current frame, and may decide whether to run based on the live `CameraConfiguration` (for example, both overlay filters respect `EnableImageOverlays`, and the mask filter checks `EnableMaskOverlay`).

## Rolling stacking buffer

`RollingFrameStacker` manages a queue of captured frames to smooth noise and improve low-light performance:

- **Capture enqueue** – Every frame is stored with its raw pixel buffer (decoded if a compressed source was supplied) and exposure duration added to the cumulative integration window.
- **Minimum depth** – The buffer keeps at least `StackingBufferMinimumFrames` frames.
- **Integration budget** – Trimming never drops below `StackingBufferIntegrationSeconds` (converted to milliseconds).
- **Stack trigger** – Once the queue holds `StackingFrameCount` frames, the newest `StackingFrameCount` frames are averaged pixel-wise (`RollingFrameStacker.AverageFrames`).
- **Fallbacks** – If buffers mismatch in size or decoding fails, the most recent frame is returned without stacking so the feed keeps flowing.

The effective integration time reported in the overlay equals the sum of the exposure durations of the frames that actually participated in the stack.

## Configuring the pipeline

### AppSettings defaults

`CameraPipelineOptions` in configuration control capture cadence, stacking, and the default filter sequence. Example snippet:

```json
"CameraPipeline": {
  "CaptureIntervalMilliseconds": 1000,
  "StackingFrameCount": 4,
  "StackingBufferMinimumFrames": 24,
  "StackingBufferIntegrationSeconds": 120,
  "EnableStacking": true,
  "EnableImageOverlays": false,
  "EnableMaskOverlay": false,
  "Filters": [
    { "Name": "CardinalDirections", "Order": 1, "Enabled": false },
    { "Name": "CelestialAnnotations", "Order": 2, "Enabled": false },
    { "Name": "OverlayText", "Order": 3, "Enabled": false },
    { "Name": "CircularMask", "Order": 4, "Enabled": false }
  ],
  "FrameFilters": [],
  "OverlayTextFormat": "yyyy-MM-dd HH:mm:ss 'UTC'"
}
```

`Filters` lets you toggle individual filters and control their default order without editing code. Only entries flagged `Enabled` are applied (sorted by `Order`). The legacy `FrameFilters` string array remains as a fallback for backward compatibility and is ignored when `Filters` contains at least one enabled entry. When both collections are empty the runtime keeps the live sequence supplied via runtime updates—when overlays are disabled (the default), the capture pipeline simply returns the raw starfield image from the camera adapter.

`CardinalDirections` provides cosmetic controls for the cardinal overlay (offset, rotation, line styling, and label boxes). Example:

```json
"CardinalDirections": {
  "OffsetXPixels": 0,
  "OffsetYPixels": 0,
  "RotationDegrees": 0,
  "RadiusOffsetPixels": 0,
  "LabelNorth": "N",
  "LabelSouth": "S",
  "LabelEast": "E",
  "LabelWest": "W",
  "SwapEastWest": false,
  "CircleColor": "#C8D2E6",
  "CircleOpacity": 170,
  "CircleThickness": 2,
  "CircleLineStyle": "LongDash",
  "LabelFillOpacity": 160,
  "LabelPadding": 6,
  "LabelCornerRadius": 6,
  "LabelFontSize": 22
}
```

- Offsets treat the image centre as (0,0); positive X moves east (right) and positive Y moves north (up).
- `RadiusOffsetPixels` nudges the synthetic horizon ring inward (negative values) or outward (positive values) relative to the simulated fisheye radius.
- Custom labels let you switch between abbreviations (`N/E/S/W`) and full words. Provide any strings up to 32 characters.
- `SwapEastWest` flips the east/west labels for mirrored optics without altering the rotation angle.
- `RadiusOffsetPixels` nudges the synthetic horizon ring inward (negative values) or outward (positive values) relative to the simulated fisheye radius.
- `CircleLineStyle` accepts `Solid`, `LongDash`, `ShortDash`, `Dotted`, or `DashDot`.
- Labels use the same colour and thickness as the circle; padding, corner radius, and fill opacity tune legibility.

`CircularMask` mirrors the same coordinate system, letting you trim the live frame with a configurable vignette:

```json
"CircularMask": {
  "OffsetXPixels": 0,
  "OffsetYPixels": 0,
  "RadiusOffsetPixels": 0,
  "MaskColor": "#000000",
  "MaskOpacity": 220
}
```

- Negative/positive radius offsets shrink or grow the clear aperture relative to the 95% auto-fit circle.
- Offsets shift the mask centre, useful when the optics aren’t perfectly concentric with the sensor.
- `MaskColor` accepts any hex colour; opacity (0–255) controls falloff strength.

  ### Observatory location

  The synthetic all-sky generator and overlay filters read the observatory latitude/longitude from the `ObservatoryLocation` section. Update these values to align the simulated sky with your deployment:

  ```json
  "ObservatoryLocation": {
    "LatitudeDegrees": 35.347,
    "LongitudeDegrees": -113.878
  }
  ```

  > **West longitudes are negative.** Restart the service (or reload configuration) after changing the location so the cached star field is rebuilt.

### Runtime updates

`CameraConfiguration` can be modified via the REST endpoint `POST /api/v1.0/all-sky/configuration`. Provide any subset of fields; unspecified properties keep their current value. To replace the filter order, send a `frameFilters` array:

```json
{
  "enableImageOverlays": true,
  "enableMaskOverlay": true,
  "frameFilters": [
    "OverlayText",
    "CardinalDirections",
    "CelestialAnnotations",
    "CircularMask"
  ]
}
```

If you toggle overlay flags without supplying `frameFilters`, the system keeps the existing order but automatically adds or removes overlay-related filters to match the boolean toggles. This preserves backwards compatibility with simple UI switches.

## Capture-to-display lifecycle

### Sequence diagram

```mermaid
sequenceDiagram
    participant Scheduler as AllSkyCaptureService
    participant Exposure as IExposureController
    participant Camera as ICameraAdapter
    participant Stacker as RollingFrameStacker
    participant Pipeline as FrameFilterPipeline
    participant Filter as IFrameFilter (sequence)
    participant Store as FrameStateStore
    participant ApiUi as API / Blazor Client

    Scheduler->>Exposure: CreateNextExposure(configuration)
    Exposure-->>Scheduler: ExposureSettings
    Scheduler->>Camera: CaptureAsync(exposure)
    Camera-->>Scheduler: CameraFrame (raw JPEG + pixels)
    Scheduler->>Stacker: Accumulate(frame, configuration)
    Stacker-->>Scheduler: FrameStackResult (stacked frame + metadata)
    Scheduler->>Pipeline: ProcessAsync(stackResult, configuration)
    loop Filter sequence
        Pipeline->>Filter: ApplyAsync(bitmap)
        Filter-->>Pipeline: Modified bitmap
    end
    Pipeline-->>Scheduler: ProcessedFrame (JPEG + overlays)
    Scheduler->>Store: UpdateFrame(raw, processed)
    Store-->>ApiUi: Latest frame + status (on request)
```

### Flow chart

```mermaid
flowchart LR
    A[Timer tick] --> B{Stacking enabled?}
    B -- No --> C[Capture frame]
    B -- Yes --> C
    C --> D[RollingFrameStacker\nAccumulate]
    D --> E{Enough frames/integration?}
    E -- No --> F[Return latest frame]
    E -- Yes --> G[Average frames]
    F --> H[FrameFilterPipeline]
    G --> H
    H --> I[Apply configured filters in order]
    I --> J[Encode JPEG]
    J --> K[FrameStateStore]
    K --> L[API / UI consumes frames]
```

## Project layout highlights

```
src/HVO.SkyMonitorV5.RPi/
├── Cameras/
│   └── MockFisheyeCameraAdapter.cs # Synthetic fisheye all-sky generator
├── HostedServices/
│   └── AllSkyCaptureService.cs    # Background capture loop
├── Pipeline/
│   ├── FrameFilterPipeline.cs     # Orchestrates filter execution
│   ├── RollingFrameStacker.cs     # Rolling buffer & averaging
│   └── Filters/                   # Individual filter implementations
├── Options/
│   ├── CameraPipelineOptions.cs   # Capture/stacking defaults
│   └── ObservatoryLocationOptions.cs # Latitude/longitude settings for synthetic sky
├── Models/                        # DTOs returned by APIs and pipeline
└── Program.cs                     # Service registration & configuration
```

## Running locally

```bash
# From the repository root
dotnet build src/HVO.SkyMonitorV5.RPi/HVO.SkyMonitorV5.RPi.csproj -c Debug

# Run the capture host
dotnet run --project src/HVO.SkyMonitorV5.RPi/HVO.SkyMonitorV5.RPi.csproj -c Debug
```

### Useful endpoints

```bash
# Download the most recent processed frame
curl http://localhost:5136/api/v1.0/all-sky/frame/latest --output processed.jpg

# Download the raw (unfiltered) frame
curl "http://localhost:5136/api/v1.0/all-sky/frame/latest?raw=true" --output raw.jpg

# Update configuration (toggle mask + reorder filters)
curl -X POST http://localhost:5136/api/v1.0/all-sky/configuration \
  -H "Content-Type: application/json" \
  -d '{"enableMaskOverlay":true,"frameFilters":["CardinalDirections","OverlayText","CircularMask"]}'
```

## Further reading

- `Program.cs` – service registration and DI wiring
- `Pipeline/Filters/*` – reference filter implementations
- `Controllers/v1_0/AllSkyController.cs` – REST API for status and configuration