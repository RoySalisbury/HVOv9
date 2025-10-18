#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="${REPO_ROOT}/src/HVO.SkyMonitorV5.RPi/Data/catalogs"
DST_DIR="${REPO_ROOT}/datastores/catalogs"

mkdir -p "${DST_DIR}"
for file in "${SRC_DIR}"/*.sqlite; do
  base="$(basename "${file}")"
  if [[ ! -f "${DST_DIR}/${base}" ]]; then
    cp "${file}" "${DST_DIR}/${base}"
    echo "Copied ${base} to ${DST_DIR}"
  fi
done
