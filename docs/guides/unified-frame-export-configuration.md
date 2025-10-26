# SkyMonitor v5: Unified Frame Export Configuration

This guide explains how to configure frame export formats per pipeline stage using the unified model introduced in HVOv9.

## Overview

There are four configurable export points:
- Raw/Archive
- Raw/Delivery (optional)
- Processed/Archive
- Processed/Delivery (optional)

Each point uses the same ImageEncodingSettings structure and can target any supported format:
- Raster: Jpeg, Png
- Scientific: Fits (with options)
- Future: Tiff, Xisf

UI endpoints always use a raster format for on-page display. When a non-raster format is configured for delivery, it will be coerced to a sensible raster default for UI preview. Download links use the configured archive format.

## Configuration (appsettings.json)

```json
{
  "FrameExport": {
    "Raw": {
      "Enabled": true,
      "PayloadScope": "ArchiveOnly",
      "ArchiveEncoding": {
        "Format": "Fits",
        "Quality": 100,
        "FitsOptions": {
          "BitDepth": "U16",
          "ImageFormat": "Mono",
          "Compression": "Rice",
          "UnsignedU16": true,
          "WriteChecksum": true
        }
      },
      "DeliveryEncoding": null
    },
    "Processed": {
      "Enabled": true,
      "PayloadScope": "ArchiveOnly",
      "ArchiveEncoding": { "Format": "Jpeg", "Quality": 95 },
      "DeliveryEncoding": { "Format": "Jpeg", "Quality": 85 }
    }
  }
}
```

Notes:
- When DeliveryEncoding is null, the ArchiveEncoding is used for all exports (single-payload mode).
- When DeliveryEncoding is set, two payloads are generated: archive and delivery.
- If a non-raster DeliveryEncoding is configured, the system will fall back to Jpeg for UI display only; downloads continue to use the archive payload as configured.

## FITS Options

When Format = Fits, additional options can be provided:
- BitDepth: U8, U16, I16, I32, F32, F64
- ImageFormat: Mono, Rgb, Rgba, BayerMosaic
- Compression: None, Rice, Gzip1, Gzip2, HCompress, PLio (mapped to available CFITSIO codecs as supported)
- UnsignedU16: true/false (applies to U16)
- WriteChecksum: true/false

## Defaults

If omitted, defaults are applied per stage during startup:
- Raw.ArchiveEncoding: Fits U16, Quality 100 (Mono, None compression)
- Processed.ArchiveEncoding: Jpeg 95
- PayloadScope: ArchiveOnly for both stages

## Migration from Legacy FitsExportOptions

Legacy settings under `FitsExport` are automatically migrated at startup via a post-configure step:
- EnableForRaw -> Raw.ArchiveEncoding = Fits (with mapped options)
- EnableForProcessed -> Processed.ArchiveEncoding = Fits (with mapped options)

You can retain `FitsExport` in configuration for a transitional period. The migration is logged at Information level with before/after summaries. New deployments should move fully to `FrameExport`.

## Validation and Startup

- Options are validated against data annotations and validated on startup.
- Any invalid configuration entries will cause a startup validation failure with details in logs.

## Tips

- Prefer DeliveryEncoding = Jpeg 80–90 for UI responsiveness when ArchiveEncoding uses Fits or other heavy formats.
- Keep thumbnails (ImageHistory) enabled to aid browsing; archive payloads are separate and unaffected.
- When preparing for color FITS, set FitsOptions.ImageFormat = Rgb and ensure upstream processing outputs match.
