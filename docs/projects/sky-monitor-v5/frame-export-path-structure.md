# Frame Export Path Structure

> **Document Status**: Technical reference for frame export path construction  
> **Last Updated**: 2025-10-26  
> **Related**: `scripts/configure-frame-export-paths.sh`, `FrameExportOptions`, `S3FrameExportSink`, `FilesystemFrameExportSink`

---

## Overview

The frame export system uses a hierarchical path structure that combines configuration values with automatically-generated components to organize exported frames by stage, role, and date.

## Path Components

### 1. Configuration-Provided Components

These are specified in `FrameExportOptions` (either via `appsettings.json` or database configuration):

- **RootPath** (Filesystem only): Base directory for all exports
  - Example: `/workspaces/HVOv9/artifacts/skymonitor`
  - Example (production): `/mnt/observatory/skymonitor/exports`

- **Prefix** (Both Filesystem and S3): Stage identifier or organizational prefix
  - Example: `raw` (for raw frames)
  - Example: `processed` (for processed frames)
  - Can be multi-segment: `observatory-1/raw`

- **Bucket** (S3 only): S3/MinIO bucket name
  - Example: `hvo-skymonitor`

### 2. Code-Generated Components

The export sinks automatically append these components:

- **Role Directory**: Determined by `PayloadScope` configuration
  - `archive` - Long-term preservation copies
  - `delivery` - UI/API delivery copies (when dual-scope enabled)

- **Date Hierarchy**: Based on frame timestamp (UTC)
  - `YYYY` - Year (e.g., `2025`)
  - `MM` - Month (e.g., `10`)
  - `DD` - Day (e.g., `26`)

- **Filename**: Constructed from timestamp and frame ID
  - Format: `YYYYMMDD-HHMMSS-{frameId}.{ext}`
  - Example: `20251026-143022-019a1fe9-2a80-7b59-8e25-a05b89c2c1bb.fits`

## Path Construction Logic

### Filesystem Paths

**Configuration:**
```json
{
  "RootPath": "/workspaces/HVOv9/artifacts/skymonitor",
  "Prefix": "raw"
}
```

**Code Logic (FrameExportFilesystemPathHelper.cs):**
1. Start with `RootPath`
2. Append `Prefix` segments (split by `/` or `\`)
3. Append role directory (`archive` or `delivery`)
4. Append date hierarchy (`YYYY/MM/DD`)
5. Append filename

**Final Path:**
```
/workspaces/HVOv9/artifacts/skymonitor/raw/archive/2025/10/26/20251026-143022-{guid}.fits
```

### S3 Object Keys

**Configuration:**
```json
{
  "Bucket": "hvo-skymonitor",
  "Prefix": "raw"
}
```

**Code Logic (S3FrameExportSinkOptions.BuildObjectPrefix):**
1. Start with `Prefix` segments (split by `/`)
2. Append role directory (`archive` or `delivery`)
3. Append date hierarchy (`YYYY/MM/DD`)
4. Append filename

**Final Object Key:**
```
raw/archive/2025/10/26/20251026-143022-{guid}.fits
```

**Full S3 URI:**
```
s3://hvo-skymonitor/raw/archive/2025/10/26/20251026-143022-{guid}.fits
```

## Configuration Examples

### Example 1: Simple Single-Root Configuration

**Configuration:**
```json
{
  "FrameExport": {
    "Raw": {
      "Filesystem": [{
        "RootPath": "/mnt/exports",
        "Prefix": "raw"
      }],
      "S3": [{
        "Bucket": "observatory",
        "Prefix": "raw"
      }]
    }
  }
}
```

**Resulting Paths:**
- Filesystem: `/mnt/exports/raw/archive/2025/10/26/20251026-143022-{guid}.fits`
- S3: `s3://observatory/raw/archive/2025/10/26/20251026-143022-{guid}.fits`

### Example 2: Multi-Site Configuration

**Configuration:**
```json
{
  "FrameExport": {
    "Raw": {
      "Filesystem": [{
        "RootPath": "/mnt/exports",
        "Prefix": "site-1/raw"
      }],
      "S3": [{
        "Bucket": "multi-site-observatory",
        "Prefix": "site-1/raw"
      }]
    }
  }
}
```

**Resulting Paths:**
- Filesystem: `/mnt/exports/site-1/raw/archive/2025/10/26/20251026-143022-{guid}.fits`
- S3: `s3://multi-site-observatory/site-1/raw/archive/2025/10/26/20251026-143022-{guid}.fits`

### Example 3: Dual-Scope (Archive + Delivery)

**Configuration:**
```json
{
  "FrameExport": {
    "Processed": {
      "PayloadScope": "ArchiveAndDelivery",
      "ArchiveEncoding": { "Format": "Fits", "Quality": 100 },
      "DeliveryEncoding": { "Format": "Jpeg", "Quality": 85 },
      "Filesystem": [{
        "RootPath": "/mnt/exports",
        "Prefix": "processed"
      }]
    }
  }
}
```

**Resulting Paths (same frame, two files):**
- Archive: `/mnt/exports/processed/archive/2025/10/26/20251026-143022-{guid}.fits`
- Delivery: `/mnt/exports/processed/delivery/2025/10/26/20251026-143022-{guid}.jpg`

## Common Mistakes

### ❌ Incorrect: Including Role in RootPath/Prefix

**BAD Configuration:**
```json
{
  "RootPath": "/mnt/exports/raw/archive",  // ❌ Don't include /archive
  "Prefix": null
}
```

**Result:** Duplicate path segments
```
/mnt/exports/raw/archive/archive/2025/10/26/...  ❌
```

### ✅ Correct: Let Code Add Role Directory

**GOOD Configuration:**
```json
{
  "RootPath": "/mnt/exports",  // ✅ Base path only
  "Prefix": "raw"               // ✅ Stage identifier
}
```

**Result:** Clean path structure
```
/mnt/exports/raw/archive/2025/10/26/...  ✅
```

## Configuration Precedence

⚠️ **Important**: Database configuration (`system_setting` table) currently overrides `appsettings.json` via `DatabaseBackedConfigurationOptionsConfigurator`.

**Resolution Order:**
1. Database (`frame-export` key in `system_setting` table)
2. `appsettings.{Environment}.json`
3. `appsettings.json`

See [TODO.md](../../TODO.md) for pending architectural decision on configuration precedence.

## Validation

To verify your configuration is correct:

1. **Check Database Configuration:**
   ```bash
   sqlite3 /path/to/sm-config.db \
     "SELECT payload_json FROM system_setting WHERE key = 'frame-export';" | jq .
   ```

2. **Verify S3 is Enabled:**
   ```bash
   sqlite3 /path/to/sm-config.db \
     "SELECT payload_json FROM system_setting WHERE key = 'frame-export';" | \
     jq '.Raw.S3[0].Enabled, .Processed.S3[0].Enabled'
   ```

3. **Check Filesystem Paths:**
   ```bash
   sqlite3 /path/to/sm-config.db \
     "SELECT payload_json FROM system_setting WHERE key = 'frame-export';" | \
     jq '.Raw.Filesystem[0] | {RootPath, Prefix}'
   ```

## Troubleshooting

### Issue: Duplicate Path Segments

**Symptom:** Paths like `/exports/raw/archive/archive/2025/...`

**Cause:** RootPath or Prefix includes role directories

**Fix:** Remove `archive` or `delivery` from RootPath/Prefix

### Issue: "sink s3 no longer supports stage Raw"

**Symptom:** Retry service warnings about unsupported stages

**Causes:**
1. S3 configuration changed from enabled to disabled
2. S3 credentials missing or invalid
3. Database and appsettings mismatch

**Fix:**
1. Verify S3 is enabled in database configuration
2. Ensure credentials are present (AccessKey, SecretKey, Endpoint)
3. Sync database and appsettings configurations

### Issue: Missing Files in Expected Locations

**Symptom:** Frames not appearing in expected directories

**Troubleshooting:**
1. Check export logs for errors: `grep "export" /path/to/logs/skymonitor-*.log`
2. Verify PayloadScope includes desired role (Archive/Delivery)
3. Confirm export is enabled: `"Enabled": true`
4. Check retry queue: `SELECT * FROM frame_export_retry;`

## Related Files

- **Configuration Script**: `scripts/configure-frame-export-paths.sh`
- **Path Helpers**:
  - `Exports/Sinks/FrameExportFilesystemPathHelper.cs`
  - `Options/S3FrameExportSinkOptions.BuildObjectPrefix()`
  - `Exports/FrameExportPathUtilities.cs`
- **Sinks**:
  - `Exports/Sinks/FilesystemFrameExportSink.cs`
  - `Exports/Sinks/S3FrameExportSink.cs`
- **Options**: `Options/FrameExportOptions.cs`

## References

- [Frame Export Project Plan](../../archive/completed-projects/frame-export-project-plan.md)
- [SkyMonitor V5 Operations Runbook](skymonitor-v5-operations-runbook.md)
- [Frame Export Retry Service](../../TODO.md#frame-export--remote-dispatch)
