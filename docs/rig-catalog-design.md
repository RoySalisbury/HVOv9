# SkyMonitor Rig Catalog Architecture

_Last updated: 2025-10-11_

## Goals
- Treat a `RigSpec` as the mount-level description that owns pointing (boresight), optics, and the attached camera.
- Move camera metadata (descriptor + capabilities) into `CameraSpec` so every camera definition is self-contained.
- Support multiple rig definitions in configuration with a single active selection for runtime.
- Eliminate duplicated camera and lens metadata by introducing reusable catalogs.
- Prepare the capture pipeline for a unified `CameraAdapter` while enforcing that only one adapter runs at a time.

## Phase plan overview
1. **Phase 1 – Domain Model Prep** — ✅ *Completed 2025-10-10*
  - Extend `RigSpec` with `BoresightAltDeg` / `BoresightAzDeg` fields.
  - Relocate `CameraDescriptor` to `CameraSpec` and expose descriptor/capability helpers.
  - Introduce catalog option POCOs (`CameraCatalogOptions`, `LensCatalogOptions`, `RigCatalogOptions`) and service interfaces to resolve specs.
  - Update `RigFactory` and projector wiring to honor boresight data.
  - Add validation helpers for the new option types.

2. **Phase 2 – Configuration & Migration** — ✅ *Completed 2025-10-10*
   - Reworked `appsettings.json` to use catalog-based configuration.
   - Provided migration guidance and temporary shims for legacy settings.
   - Enforced validation on startup (missing references, duplicate names, invalid boresight ranges).
   - Improved logging to surface configuration load status.
  - ✅ *Kickoff:* Added `AllSkyCatalogOptions` scaffolding with camera/lens/rig catalog entries and catalog service interfaces.
  - ✅ *Latest:* Registered catalog services in DI and created the in-memory catalog registry for option-backed resolution.
  - ✅ *Config migrated:* `appsettings.json` now seeds camera, lens, and rig catalogs with adapters referencing catalog rigs rather than inline definitions.
  - ✅ *Validation on start:* `AllSkyCatalogOptions` now uses `ValidateOnStart`/DataAnnotations at DI registration to fail fast when catalog entries are invalid.
  - ✅ *Migration shim:* Inline rig definitions continue to load with warning logs so legacy configurations remain functional during the transition.
  - ✅ *Startup logging:* A catalog configuration reporter now emits camera/lens/rig counts and highlights outstanding migration work at host startup.

3. **Phase 3 – Adapter Unification Groundwork**
   - **Phase 3.1 – Rig Acquisition Adapter foundations** — 🚀 *In progress*
     - Define `CameraDriverId` enumeration and extend `CameraSpec` with `IsSynthetic`, driver metadata, and optional synthetic profile identifiers.
     - Establish the `IRigAcquisitionAdapter` contract plus core lifecycle actions (`StartAsync`, `PauseAsync`, `ResumeAsync`, `ReloadAsync`).
     - Wire catalog resolution so the adapter loads active `RigSpec` instances from `AllSkyCatalogRegistry` with change monitoring hooks.
  - ✅ *Kickoff:* Added `CameraDriverId`, expanded `CameraSpec`, updated catalog/inline options, introduced the baseline `RigAcquisitionAdapter` state machine with catalog change monitoring, and started wiring it through the capture hosted service.
  - ✅ *Latest:* `RigAcquisitionAdapter` now owns camera driver instantiation via `CameraDriverFactory`, exposes `CaptureAsync`, and the capture hosted service delegates all frame acquisition through the adapter.
   - **Phase 3.2 – Concurrency & exposure feedback loop** — ✅ *Completed 2025-10-11*
     - Introduced a lightweight exposure analyzer that runs between captures to recommend updated settings before the next frame.
     - Split capture and processing into distinct asynchronous stages with a bounded channel, keeping a synchronous mode toggle for migration.
     - Ensured synthetic rigs reuse the adapter for `ICamera` callbacks while real rigs dispatch to keyed DI camera drivers.
    - ✅ *Analyzer wired:* `SimpleExposureAnalyzer` now executes after each capture, logs luminance metrics, and feeds the adaptive exposure controller with TTL-scoped day/night overrides.
    - ✅ *Asynchronous pipeline:* Capture can feed a bounded processing queue, with synchronous mode retained as a fallback for legacy scenarios.
    - ✅ *Queue tuning:* Adaptive queue scaling is enabled for the background stacker, and enqueue logic now retries after channel swaps so bursty loads no longer force synchronous fallbacks.
    - 📌 *Follow-up soak:* Schedule the longer Raspberry Pi stress soak to validate the tuned queue under sustained hardware load (not blocking completion).
  - ✅ *Queue telemetry:* Asynchronous processing now publishes queue depth, backpressure, and processing latency metrics to the status API and dashboard so operators can monitor decoupled pipeline health.
   - **Phase 3.3 – Distributed processing readiness**
     - Abstract pipeline processing so frames can be published to a queue (e.g., RabbitMQ) when off-host execution is enabled.
     - Add diagnostics covering capture backpressure, rig mode changes, and adapter reload events.
     - Document per-rig pipeline override hooks in the catalog for future filter customization.
    - ✅ *Remote dispatch scaffold:* The capture service now pushes frame envelopes through a remote dispatch publisher, surfaces telemetry in the dashboard, and logs outcomes. The initial implementation targets an S3-backed fan-out path with RabbitMQ wiring planned for the next increment.

4. **Phase 4 – Single Adapter Transition**
   - Collapse dedicated adapter implementations into strategy methods inside the unified adapter.
   - Enforce the "single active adapter" rule at host startup.
   - Update stress/integration harnesses to the catalog configuration.

5. **Phase 5 – Tests & Benchmarks**
   - Finalize unit/integration coverage and performance measurements after feature work stabilizes.
   - Remove transitional configuration shims and deprecated presets.

## Sample configuration
```jsonc
{
  "AllSkyCatalogs": {
    "Cameras": [
      {
        "Name": "ASI174MC",
        "Sensor": {
          "WidthPx": 1936,
          "HeightPx": 1216,
          "PixelSizeMicrons": 5.86
        },
        "Capabilities": {
          "ColorMode": "Color",
          "SensorTechnology": "Cmos",
          "BodyType": "DedicatedAstronomy",
          "Cooling": "Regulated",
          "SupportsGainControl": true,
          "SupportsExposureControl": true,
          "SupportsTemperatureTelemetry": true,
          "SupportsSoftwareBinning": true,
          "AdditionalTags": [ "HighSpeed" ]
        },
        "Descriptor": {
          "Manufacturer": "ZWO",
          "Model": "ASI174MC-Pro",
          "DriverVersion": "1.12.3",
          "AdapterName": "ZwoCameraAdapter",
          "Capabilities": [ "NativeHardware", "StackingCompatible", "HighSpeed", "Cooled" ]
        }
      }
    ],
    "Lenses": [
      {
        "Name": "Fujinon_FE185C086HA_1",
        "Model": "Equidistant",
        "FocalLengthMm": 2.7,
        "FovXDeg": 185.0,
        "FovYDeg": 185.0,
        "RollDeg": 0.0,
        "Kind": "Fisheye"
      }
    ],
    "Rigs": {
      "ActiveRig": "MockFisheye",
      "Entries": [
        {
          "Name": "MockFisheye",
          "Camera": "MockASI174MM",
          "Lens": "Fujinon_FE185C086HA_1",
          "BoresightAltDeg": 90.0,
          "BoresightAzDeg": 0.0
        }
      ]
    }
  }
}
```

## Considerations
- Surface validation failures early via `IValidateOptions`; include the offending catalog entry name in error messages.
- Decide on sensible defaults for boresight when migrating legacy rigs (likely zenith `90°/0°`).
- Catalog growth may require lazy loading if future rigs include large calibration payloads (distortion maps, flats).
- Keep descriptors free of sensitive data (serial numbers, IPs) or sanitize logs if those fields are introduced.
- **Phase 2 groundwork (2025-10-10):** Introduced catalog option POCOs and catalog service interfaces to begin migrating configuration off adapter-specific rigs.
- Persist the latest `ExposureAnalysisResult` in `FrameStateStore` so UI/API surfaces can display analyzer metrics alongside capture telemetry.
- SkyMonitor v5 home view now includes an exposure analysis card summarising lighting state, luminance metrics, and active recommendations for the operator.
- Synthetic camera catalog entries must now specify both `DriverId = "Synthetic"` and a `SyntheticProfile` so rig adapters can resolve the correct mock driver during validation.
- Simple exposure analyzer is now clamped by adaptive controller smoothing so per-frame recommendations ease toward the base profile instead of jumping directly to the suggested value.
- Adaptive controller now reports day/night override snapshots to `FrameStateStore`, and the SkyMonitor v5 dashboard surfaces applied vs baseline exposure for each bucket with TTL context.
- **Phase 3 architecture decisions (2025-10-10):**
  - Adopt the term `RigAcquisitionAdapter` for the unified facade responsible for selecting real vs synthetic capture flows.
  - `CameraSpec` gains a `CameraDriverId` enum reference (currently `Unknown`, `Synthetic`, or `Zwo`), `IsSynthetic` flag, and optional synthetic profile name so keyed DI can locate the correct driver or generator.
  - Synthetic rigs reuse the adapter instance as the `ICamera` callback target; real rigs obtain drivers through keyed DI registrations.
  - The adapter lifecycle supports hot pause/resume and rig reloads without process restarts, coordinating with catalog change notifications.
  - Capture dispatch and pipeline processing are decoupled via an asynchronous queue; a quick exposure analyzer runs inline before enqueueing the frame for heavier processing.
  - Pipeline stages remain per-rig configurable, with override hooks reserved for future filter tuning.
- **Phase 1 implementation notes (2025-10-10):**
  - `RigSpec` now includes `BoresightAltDeg`/`BoresightAzDeg` with zenith defaults and exposes `Camera.Descriptor` directly.
  - `CameraSpec` owns descriptor metadata; presets and adapter options were updated to supply descriptors centrally.
  - Adapter constructors (mock + ZWO) enrich descriptors only when catalogs omit manufacturer data, ensuring consistent logging while honoring configured overrides.
  - Configuration binding accepts boresight angles and feeds them through to projector helpers, unblocking Phase 2 catalog work.

## Future enhancements
- Support hot-swapping rigs (or boresight offsets) without restarting the host.
- Attach calibration artifacts (distortion models, flat fields) to camera/lens combos within the catalogs.
- Introduce mount diagnostics (e.g., encoders, weather) once hardware integration begins.
- Explore persistent catalog storage (database or Git-backed configuration) for multi-site observatory deployments.

### Phase 2 migration guidance
- **Catalog first:** Create entries under `AllSkyCatalogs` for each camera, lens, and rig. Use the new catalog reporter logs to confirm counts and the active rig.
- **Swap adapters:** Update each `AllSkyCameras` entry to point to the catalog rig via `RigCatalog`. Remove inline rig blocks once validated.
- **Monitor logs:** Legacy inline rigs trigger startup warnings; treat them as a checklist for remaining migration work.
- **Validate:** Startup now fails fast when catalog entries are inconsistent—resolve any errors surfaced during host boot before proceeding to later phases.
```