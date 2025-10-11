# SkyMonitor Rig Catalog Architecture

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

2. **Phase 2 – Configuration & Migration** — 🚧 *In progress*
   - Rework `appsettings.json` to use catalog-based configuration.
   - Provide migration guidance and temporary shims for legacy settings.
   - Enforce validation on startup (missing references, duplicate names, invalid boresight ranges).
   - Improve logging to surface configuration load status.
  - ✅ *Kickoff:* Added `AllSkyCatalogOptions` scaffolding with camera/lens/rig catalog entries and catalog service interfaces.
  - ✅ *Latest:* Registered catalog services in DI and created the in-memory catalog registry for option-backed resolution.
  - ✅ *Config migrated:* `appsettings.json` now seeds camera, lens, and rig catalogs with adapters referencing catalog rigs rather than inline definitions.
  - ✅ *Validation on start:* `AllSkyCatalogOptions` now uses `ValidateOnStart`/DataAnnotations at DI registration to fail fast when catalog entries are invalid.

3. **Phase 3 – Adapter Unification Groundwork**
   - Switch adapters to consume catalog output and camera descriptors from `CameraSpec`.
   - Introduce a facade `CameraAdapter` that chooses concrete behavior based on camera capabilities.
   - Update status reporting/UI to rely on the relocated descriptor and new rig orientation data.

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
```