# SkyMonitor v5 synthetic starfield pipeline

This note documents the latest improvements to the mock fisheye camera so we have a clear baseline of how synthetic frames are assembled.

## Candidate selection

1. **Catalog query** – `SkyMonitorRepository` collects all stars under the configured magnitude limit that can rise above `MinMaxAltitudeDegrees` for the observatory.
2. **Bright / mid / faint bands** – The adapter requests three subsets:
   - *Bright* (≈4 % of the requested total, capped at magnitude ≤ 2).
   - *Mid* (≈20 %, stratified using the configured right-ascension bins and declination bands).
   - *Faint* (fill the remainder, over-sampled and later thinned in screen space).
3. **Constellation highlights** – When enabled, a handful of asterism members per constellation are injected before the final selection pass.
4. **Screen-space balancing** – Every candidate is projected through the same fisheye geometry used for rendering. The `StarFieldEngine` now creates and owns the fisheye projector directly from the active `RigSpec`, so selection, filtering, and rendering all share the exact lens model. Concentric horizon rings own fixed quotas (inner→outer ≈6 %, 10 %, 18 %, 28 %, 38 %). Stars must also respect a magnitude-aware minimum separation (18 px for first magnitude, 10 px for third, 5 px for faint).
5. **Backfill** – If a quota cannot be met (e.g., the catalog is sparse near the horizon) the adapter performs a secondary pass that only applies the separation test so the final list always fills the requested `TopStarCount`.

## Rendering

- **Projection & refraction** – `StarFieldEngine` now supports Bennett atmospheric refraction and constructs the fisheye projector from the configured rig (default FOV ≈185 °). Because the engine owns the projector, any consumer pulling `FrameRenderContext.Projector` sees the exact geometry used during rendering.
- **Star sizing** – A `StarSizeCurve` logistic maps magnitude to radius using tunable min/max/transition parameters. Very bright stars gain a small linear boost so they retain presence without blowing out.
- **Microdots** – Stars fainter than ~5.2 mag or with a radius under 1.05 px are drawn as 1×1 pixels; targets past 6 mag expand to 2×2 blocks. The change keeps faint stars visible after scaling or video encoding while preserving the anti-aliased look for brighter magnitudes.
- **Colour handling** – When `dimFaintStars` is enabled the engine reduces alpha rather than clamping RGB values. Combined with the new sensor-noise routine (below), spectral hues survive simulated gain boosts.
- **Planets** – Planet glyphs retain the previous behaviour (shape switch plus Moon radius clamp) but inherit the new projection, refraction, and flip settings.

## Sensor noise

`ApplySensorNoise` now operates in luminance space. It scales the RGB triple by a random factor between 0.6 and 1.4 and adds a small twinkle probability that depends on the simulated gain. This produces organic twinkling while keeping white balance largely intact.

## Configuration tips

- `StarCatalog.TopStarCount` is treated as a lower bound; the adapter guarantees at least 300 candidates even if the configuration requests fewer. Increase it if you want denser skies—the ring quotas adapt automatically.
- `RightAscensionBins` and `DeclinationBands` directly affect the mid-tier stratification. Reduce them for looser clustering, or increase them when you want an evenly tiled dome.
- Use `IncludeConstellationHighlight` sparingly; each enabled constellation contributes up to `ConstellationStarCap` of its brightest members on top of the normal quota.
- To experiment with different star size behaviour, adjust the `StarSizeCurve` parameters when instantiating `StarFieldEngine`.

## Multi-adapter configuration

- Camera adapters are now registered from configuration under the top-level `AllSkyCameras` array. Each entry supplies a unique `Name`, the adapter `Adapter` identifier (for example `Mock` or `Zwo`), and the full `Rig` definition (sensor + lens) that should be injected into that adapter instance.
- The DI container creates a keyed scope for every configured adapter. The capture host resolves the first declared camera as the default, so list the primary adapter first when running multiple cameras in the same process.
- Example snippet with two adapters sharing the same executable:

   ```json
   "AllSkyCameras": [
      {
         "Name": "MockFisheye",
         "Adapter": "Mock",
         "Rig": {
            "Name": "MockASI174MM + Fujinon 2.7mm",
            "Sensor": { "WidthPx": 1936, "HeightPx": 1216, "PixelSizeMicrons": 5.86 },
            "Lens": { "Model": "Equidistant", "FocalLengthMm": 2.7, "FovXDeg": 185.0, "FovYDeg": 185.0, "RollDeg": 0.0, "Kind": "Fisheye" },
            "Descriptor": {
               "Manufacturer": "HVO",
               "Model": "Mock Fisheye AllSky",
               "DriverVersion": "2.0.0",
               "AdapterName": "MockCameraAdapter",
               "Capabilities": [
                  "Synthetic",
                  "StackingCompatible",
                  "FisheyeProjection"
               ]
            }
         }
      },
      {
         "Name": "NorthRig",
         "Adapter": "Mock",
         "Rig": {
            "Name": "Custom Observatory Lens",
            "Sensor": { "WidthPx": 4096, "HeightPx": 2160, "PixelSizeMicrons": 3.45 },
            "Lens": { "Model": "Equisolid", "FocalLengthMm": 4.0, "FovXDeg": 210.0, "RollDeg": 5.0, "Kind": "Fisheye" }
         }
      }
   ]
   ```

- Adapters retrieve their rig directly from configuration, so there is no longer a global `IRigProvider`. Any future hardware adapter simply needs to expose a constructor that accepts its `RigSpec`.
- Each rig can now embed a `CameraDescriptor`, letting the configuration describe manufacturer/model information that the capture host surfaces through the API.

## Reference

- Candidate assembly lives in `MockCameraAdapter.CaptureAsync`.
- Projection, sizing, and rendering logic is in `Cameras/Rendering/StarFieldEngine.cs`.
- The raw catalog queries are implemented in `Data/SkyMonitorRepository.cs`.
