#!/usr/bin/env bash
set -euo pipefail

# Configure frame export filesystem paths in SkyMonitor configuration database
# This script sets up the export paths for archive and delivery payloads
#
# PATH STRUCTURE:
# ---------------
# Configuration provides:
#   - RootPath: Base directory (e.g., /workspaces/HVOv9/artifacts/skymonitor)
#   - Prefix: Stage identifier (e.g., "raw" or "processed")
#
# Code automatically appends:
#   - Role directory: "archive" or "delivery" (based on PayloadScope)
#   - Date hierarchy: YYYY/MM/DD
#   - Filename: YYYYMMDD-HHMMSS-{frameId}.{ext}
#
# Final path example:
#   /workspaces/HVOv9/artifacts/skymonitor/raw/archive/2025/10/26/20251026-143022-{guid}.fits
#
# DO NOT include role directories (archive/delivery) in RootPath or Prefix!
# The code will add them automatically based on the export role.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CONFIG_DB="${REPO_ROOT}/datastores/configuration/sm-config.db"
EXPORT_ROOT="${REPO_ROOT}/artifacts/skymonitor"

# Optional S3/MinIO configuration from environment
MINIO_ENDPOINT="${HVO_MINIO_ENDPOINT:-}"
MINIO_ACCESS_KEY="${HVO_MINIO_ACCESS_KEY:-}"
MINIO_SECRET_KEY="${HVO_MINIO_SECRET_KEY:-}"
MINIO_REGION="${HVO_MINIO_REGION:-us-west-2}"
MINIO_USE_SSL=false
if [[ -n "$MINIO_ENDPOINT" ]]; then
  if [[ "$MINIO_ENDPOINT" == https:* ]]; then
    MINIO_USE_SSL=true
  fi
fi

if [[ ! -f "$CONFIG_DB" ]]; then
    echo "Error: Configuration database not found at $CONFIG_DB"
    echo "Please start SkyMonitor at least once to initialize the database."
    exit 1
fi

echo "Configuring frame export paths..."
echo "  Database: $CONFIG_DB"
echo "  Export root: $EXPORT_ROOT"
if [[ -n "$MINIO_ENDPOINT" ]]; then
  echo "  S3 (MinIO) endpoint: $MINIO_ENDPOINT"
else
  echo "  S3 (MinIO) endpoint: (not set; S3 sinks will be created disabled)"
fi

# Create the frame export configuration JSON
if [[ -n "$MINIO_ENDPOINT" && -n "$MINIO_ACCESS_KEY" && -n "$MINIO_SECRET_KEY" ]]; then
  RAW_S3_JSON=$(cat <<EOS3
      {
        "Enabled": true,
        "Bucket": "hvo-skymonitor",
        "Prefix": "raw",
        "Endpoint": "${MINIO_ENDPOINT}",
        "AccessKey": "${MINIO_ACCESS_KEY}",
        "SecretKey": "${MINIO_SECRET_KEY}",
        "Region": "${MINIO_REGION}",
        "UseSsl": ${MINIO_USE_SSL},
        "EmitMetadataHeaders": true,
        "EmitJsonManifest": true
      }
EOS3
  )
  PROC_S3_JSON=$(cat <<EOS3
      {
        "Enabled": true,
        "Bucket": "hvo-skymonitor",
        "Prefix": "processed",
        "Endpoint": "${MINIO_ENDPOINT}",
        "AccessKey": "${MINIO_ACCESS_KEY}",
        "SecretKey": "${MINIO_SECRET_KEY}",
        "Region": "${MINIO_REGION}",
        "UseSsl": ${MINIO_USE_SSL},
        "EmitMetadataHeaders": true,
        "EmitJsonManifest": true
      }
EOS3
  )
else
  RAW_S3_JSON=$(cat <<EOS3
      {
        "Enabled": false,
        "Bucket": null,
        "Prefix": "raw",
        "Endpoint": null,
        "AccessKey": null,
        "SecretKey": null,
        "Region": "${MINIO_REGION}",
        "UseSsl": false,
        "EmitMetadataHeaders": true,
        "EmitJsonManifest": true
      }
EOS3
  )
  PROC_S3_JSON=$(cat <<EOS3
      {
        "Enabled": false,
        "Bucket": null,
        "Prefix": "processed",
        "Endpoint": null,
        "AccessKey": null,
        "SecretKey": null,
        "Region": "${MINIO_REGION}",
        "UseSsl": false,
        "EmitMetadataHeaders": true,
        "EmitJsonManifest": true
      }
EOS3
  )
fi

FRAME_EXPORT_JSON=$(cat <<EOF
{
  "Raw": {
    "Enabled": true,
    "PayloadScope": "ArchiveOnly",
    "ArchiveEncoding": {
      "Format": "Fits",
      "Quality": 100,
      "FitsOptions": {
        "BitDepth": "U16",
        "ImageFormat": "Mono",
        "Compression": "None",
        "UnsignedU16": true,
        "WriteChecksum": true
      }
    },
    "DeliveryEncoding": null,
    "Filesystem": [
      {
        "Enabled": true,
        "RootPath": "${EXPORT_ROOT}",
        "Prefix": "raw",
        "IncludeMetadataManifest": true
      }
    ],
    "S3": [
${RAW_S3_JSON}
    ]
  },
  "Processed": {
    "Enabled": true,
    "PayloadScope": "ArchiveOnly",
    "ArchiveEncoding": {
      "Format": "Jpeg",
      "Quality": 95,
      "FitsOptions": null
    },
    "DeliveryEncoding": null,
    "Filesystem": [
      {
        "Enabled": true,
        "RootPath": "${EXPORT_ROOT}",
        "Prefix": "processed",
        "IncludeMetadataManifest": true
      }
    ],
    "S3": [
${PROC_S3_JSON}
    ]
  }
}
EOF
)

# Escape JSON for SQLite (replace ' with '')
ESCAPED_JSON="${FRAME_EXPORT_JSON//\'/\'\'}"

# Update or insert the frame-export system setting
sqlite3 "$CONFIG_DB" <<SQL
INSERT INTO system_setting (key, payload_json, updated_utc, revision)
VALUES ('frame-export', '${ESCAPED_JSON}', datetime('now'), 1)
ON CONFLICT(key) DO UPDATE SET
  payload_json = excluded.payload_json,
  updated_utc = datetime('now'),
  revision = revision + 1;
SQL

echo "✓ Frame export configuration updated successfully"
echo ""
echo "Export directory structure:"
echo "  Root:      ${EXPORT_ROOT}"
echo "  Raw:       ${EXPORT_ROOT}/raw/{archive|delivery}/YYYY/MM/DD/"
echo "  Processed: ${EXPORT_ROOT}/processed/{archive|delivery}/YYYY/MM/DD/"
echo ""
echo "Example paths:"
echo "  Raw archive:       ${EXPORT_ROOT}/raw/archive/2025/10/26/20251026-143022-{guid}.fits"
echo "  Processed archive: ${EXPORT_ROOT}/processed/archive/2025/10/26/20251026-143022-{guid}.jpg"
echo ""
echo "Note: Restart SkyMonitor for changes to take effect."
