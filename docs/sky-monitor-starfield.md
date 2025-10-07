# SkyMonitor v5 synthetic starfield pipeline

This note documents the latest improvements to the mock fisheye camera so we have a clear baseline of how synthetic frames are assembled.

## Candidate selection

1. **Catalog query** – `HygStarRepository` collects all stars under the configured magnitude limit that can rise above `MinMaxAltitudeDegrees` for the observatory.
2. **Bright / mid / faint bands** – The adapter requests three subsets:
   - *Bright* (≈4 % of the requested total, capped at magnitude ≤ 2).
   - *Mid* (≈20 %, stratified using the configured right-ascension bins and declination bands).
   - *Faint* (fill the remainder, over-sampled and later thinned in screen space).
3. **Constellation highlights** – When enabled, a handful of asterism members per constellation are injected before the final selection pass.
4. **Screen-space balancing** – Every candidate is projected through the same fisheye geometry used for rendering. Concentric horizon rings own fixed quotas (inner→outer ≈6 %, 10 %, 18 %, 28 %, 38 %). Stars must also respect a magnitude-aware minimum separation (18 px for first magnitude, 10 px for third, 5 px for faint).
5. **Backfill** – If a quota cannot be met (e.g., the catalog is sparse near the horizon) the adapter performs a secondary pass that only applies the separation test so the final list always fills the requested `TopStarCount`.

## Rendering

- **Projection & refraction** – `StarFieldEngine` now supports Bennett atmospheric refraction and a configurable fisheye field of view (default 184 °), so selection and render paths produce consistent results near the horizon.
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

## Reference

- Candidate assembly lives in `MockFisheyeCameraAdapter.CaptureAsync`.
- Projection, sizing, and rendering logic is in `Cameras/MockCamera/Fisheye/StarFieldEngine.cs`.
- The raw catalog queries are implemented in `Data/HygStarRepository.cs`.
