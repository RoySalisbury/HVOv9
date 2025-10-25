# SkyMonitor V5 – FITS Export Plan

Status: Draft (in progress)
Owner: SkyMonitor V5
Last Updated: 2025-10-25

## Goal
Standardize all persisted and delivered frame payloads on FITS (Flexible Image Transport System) using the new HVO.Astronomy.CFITSIO library, with optional compression and rich astro metadata (including WCS when available).

## Scope
- Raw frame exports (dispatcher + API downloads)
- Processed frame exports (dispatcher)
- Filesystem and S3 sinks (no code changes required; consume content type/extension from envelopes)
- FITS metadata stamping (image, instrument, site, pointing, and WCS basics)

Out-of-scope (optional final phase): color-cube FITS, tiled compression knobs, advanced WCS/SIP, multi-extension bundles, large-frame streaming.

---

## Phases & Tasks

### Phase 1 — Options and DI
- [ ] Create `FitsExportOptions`
  - EnableForRaw (bool)
  - EnableForProcessed (bool)
  - BitDepth: `U16 | U8` (default U16)
  - UnsignedU16 (bool, default true → BSCALE=1, BZERO=32768)
  - Compression: `None | Rice | Gzip1 | Gzip2 | HCompress`
  - WriteChecksum (bool, default true)
- [ ] Bind options in `Program.cs`: `services.Configure<FitsExportOptions>(...)`
- [ ] Register services:
  - `services.AddSingleton<IFitsFrameEncoder, FitsFrameEncoder>()`
  - Keep `IProcessedFrameEncoder` bound, but its implementation will delegate to FITS when enabled

### Phase 2 — FITS encoder service
- [ ] Define `IFitsFrameEncoder`
  - `Result<ProcessedFrameDelivery> EncodeRaw(SKImage image, RawFrameSnapshot frame, RigSpec rig, FitsExportOptions opts)`
  - `Result<ProcessedFrameDelivery> EncodeProcessed(ProcessedFrame frame, FrameStackContext ctx, RigSpec rig, FitsExportOptions opts)`
- [ ] Implement `FitsFrameEncoder` using `HVO.Astronomy.CFITSIO`:
  - Prefer U16 with unsigned scaling; fallback to U8 when configured
  - Convert color frames to grayscale for v1 (see Optional Phase for color cube)
  - Apply compression with `fits_img_compress` when enabled (return compressed image bytes; main image usually in HDU 2)
  - Write checksum when configured
  - Stamp FITS headers (see Keywords section)

### Phase 3 — Processed export switch (Option A)
- [ ] Replace `ProcessedFrameEncoder.Encode` to output FITS when `EnableForProcessed==true`
  - Delegate to `IFitsFrameEncoder.EncodeProcessed`
  - Preserve PNG/JPEG only when disabled or on error fallback (log + metrics)
- [ ] Ensure `FrameExportPublisher` continues to work with generic `ProcessedFrameDelivery` (no changes expected)

### Phase 4 — Raw export switch
- [ ] Update `FrameExportPublisher.PublishRawFrame` to produce FITS when `EnableForRaw==true`
  - Use `IFitsFrameEncoder.EncodeRaw`
  - Set envelope to `application/fits` + `.fits`
  - Retain PNG fallback on error; record fallback via feature monitor

### Phase 5 — API download support
- [ ] Add `rawFormat=fits` to `AllSkyController.GetLatestFrame` query
  - When requested, return FITS bytes from `IFitsFrameEncoder.EncodeRaw`
  - Content-Type: `application/fits`
  - Keep RAW/PNG paths as fallback

### Phase 6 — Tests
- [ ] `ProcessedFrameEncoderTests`: FITS enabled → `application/fits`, `.fits`, non-empty payload; assert basic FITS headers (BITPIX, NAXIS1/2, DATE-OBS)
- [ ] `AllSkyControllerTests`: `rawFormat=fits` returns `application/fits` with payload
- [ ] Export pipeline tests: ensure envelopes carry correct content type/extension under FITS
- [ ] Maintain CFITSIO memfile tests (already in `HVO.Astronomy.CFITSIO`)

### Phase 7 — Database and UI Configuration
- [ ] Add `FitsExportOptions` to `DatabaseBackedConfigurationOptionsConfigurator`
  - Implement `IConfigureOptions<FitsExportOptions>` interface
  - Create new system setting key: `SystemSettingKeys.FitsExport`
  - Add `Configure(FitsExportOptions)` method to load from DB
  - Register configurator in `Program.cs` DI alongside other options
- [ ] Create EF entity for FITS export settings
  - Add migration for new system setting storage
  - Provide default seed values matching `FitsExportOptions` defaults
- [ ] Create UI configuration page for FITS export settings
  - Add route `/admin/settings/fits-export` or similar
  - Form inputs for all `FitsExportOptions` properties
  - Save endpoint using `SystemConfigurationService` pattern
  - Match UI pattern from other settings pages
- [ ] Update `SystemConfigurationService`
  - Add `GetFitsExportSettingsAsync` method
  - Add `UpdateFitsExportSettingsAsync` method
  - Follow pattern from `LocalApiClientOptions` and `TelemetryRetentionOptions`

---

### Phase 8 — Native Assets Packaging for CFITSIO

Problem: When consuming `HVO.Astronomy.CFITSIO` via ProjectReference (instead of NuGet), native CFITSIO binaries under `runtimes/**` aren’t brought into app outputs automatically. We added a local copy target in SkyMonitor to bridge this, but the clean solution is to publish native assets in a dedicated package and reference it transitively.

- [ ] Create `HVO.Astronomy.CFITSIO.NativeAssets` NuGet project (no managed code)
  - Sdk: `Microsoft.NET.Sdk`
  - `TargetFramework: net9.0`
  - `IncludeBuildOutput: false` (so no empty DLL is produced)
  - `GeneratePackageOnBuild: true`
  - Pack all native files: `runtimes/**` → `PackagePath="runtimes"`
  - Metadata: `PackageId=HVO.Astronomy.CFITSIO.NativeAssets`, license/readme tags
- [ ] Update `HVO.Astronomy.CFITSIO` (managed) to reference the NativeAssets package
  - Add `PackageReference Include="HVO.Astronomy.CFITSIO.NativeAssets"` (not PrivateAssets), so native libs flow transitively to consumers using the managed package
- [ ] For devs using ProjectReference to `HVO.Astronomy.CFITSIO`
  - Add `PackageReference Include="HVO.Astronomy.CFITSIO.NativeAssets"` to app/test projects
  - Remove the temporary MSBuild copy target in SkyMonitor once the package is in place
- [ ] Validate both consumption paths
  - NuGet-only: App references `HVO.Astronomy.CFITSIO` (managed) and gets native assets transitively
  - Source-based: App references `HVO.Astronomy.CFITSIO` (ProjectReference) and also `HVO.Astronomy.CFITSIO.NativeAssets` (PackageReference)
- [ ] CI: Build and pack both packages; optionally push to local feed `.LocalPackages/`

Acceptance:
- Native CFITSIO libraries are present in app/bin for both NuGet and ProjectReference consumers, with no custom copy targets.
- Packaging is self-contained and documented in the repo for future consumers.

---

## FITS Keywords (v1)

Write when available:

- Core image
  - `SIMPLE = T`, `BITPIX = 16|8`, `NAXIS = 2`, `NAXIS1`, `NAXIS2`
  - Unsigned 16-bit path: `BSCALE = 1`, `BZERO = 32768`
  - Optional: `BUNIT = 'ADU'`
- Timing
  - `TIMESYS = 'UTC'`
  - `DATE-OBS` (ISO 8601 UTC)
  - `MJD-OBS` (derived)
- Exposure/Camera/Optics
  - `EXPTIME` (s), `GAIN` (if known), `EGAIN` (e-/ADU if known), `CCD-TEMP` (°C)
  - `INSTRUME` (camera model), `TELESCOP` (rig), `FILTER` (if applicable)
  - `XPIXSZ`, `YPIXSZ` (µm), `XBINNING`, `YBINNING`
  - `FOCALLEN` (mm), optional `PIXSCALE` (arcsec/pixel)
- Site
  - `OBSGEO-LAT`, `OBSGEO-LON` (deg), `OBSGEO-ALT` (m)
- Pointing
  - `RA` (deg), `DEC` (deg)
  - `OBJCTRA`, `OBJCTDEC` (sexagesimal)
  - `RADECSYS='ICRS'`, `EQUINOX=2000.0` (when appropriate)
  - `ALTITUDE` (deg), `AZIMUTH` (deg)
- WCS (when inputs are available)
  - `CRVAL1`, `CRVAL2` (deg)
  - `CRPIX1`, `CRPIX2`
  - Either CD matrix (`CD1_1`,`CD1_2`,`CD2_1`,`CD2_2`) or `CDELT1`,`CDELT2` + `CROTA2`
  - `CTYPE1='RA---TAN'`, `CTYPE2='DEC--TAN'`

---

## Optional Final Phase (Edge Cases)

Enable selectively after core FITS delivery is stable.

- [ ] Color-preserving FITS:
  - Write NAXIS=3 color cube (R,G,B planes) instead of grayscale for color sensors
- [ ] Tiled compression tuning:
  - Expose tile dimension options; call `fits_set_tile_dimll` if available in build
- [ ] Advanced WCS / Plate solve integration:
  - Write full TAN/SIP WCS including distortion terms (PV/SIP coefficients)
- [ ] Multi-extension archival FITS:
  - Package RAW and PROCESSED as separate HDUs (MEF) for archival backends
- [ ] Format negotiation & policy:
  - Per-sink overrides (e.g., S3 compressed, filesystem uncompressed), API Accept negotiation
- [ ] Archive backfill tools:
  - Batch migrate legacy PNG/JPEG/skimg to FITS with metadata stamping
- [ ] Performance & throughput:
  - Benchmarks for compression choices; consider streaming writer for very large frames

---

## Acceptance Criteria

- When enabled, raw and processed exports deliver `application/fits` with `.fits` extension via dispatcher, stored by sinks without code changes
- API endpoint `GET /api/v1.0/all-sky/frame/latest?raw=true&rawFormat=fits` returns FITS bytes
- FITS headers contain core image metadata and astro fields; WCS basics written when data provided
- Full test suite green; new tests cover FITS paths

## Rollout Notes

- No runtime compatibility constraints (pre-1.0)
- PNG/JPEG paths remain as fallback only
- Document HDU placement for compressed images (image often in HDU #2)
