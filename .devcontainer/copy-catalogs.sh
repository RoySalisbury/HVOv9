#!/usr/bin/env bash
# Copy catalog SQLite files to the dev-writable data directory if not already present
set -e

SRC_DIR="/workspaces/HVOv9/src/HVO.SkyMonitorV5.RPi/Data/catalogs"
DST_DIR="/workspaces/HVOv9/datastores/catalogs"

mkdir -p "$DST_DIR"
for file in "$SRC_DIR"/*.sqlite; do
  base="$(basename "$file")"
  if [ ! -f "$DST_DIR/$base" ]; then
    cp "$file" "$DST_DIR/$base"
    echo "Copied $base to $DST_DIR"
  fi
done
