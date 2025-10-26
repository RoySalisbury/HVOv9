#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${PROJECT_DIR}/../../.." && pwd)"

SRC_DIR="${PROJECT_DIR}/Data/catalogs"
DST_DIR="${REPO_ROOT}/datastores/catalogs"

mkdir -p "${DST_DIR}"
for file in "${SRC_DIR}"/*.sqlite; do
  base="$(basename "${file}")"
  if [[ ! -f "${DST_DIR}/${base}" ]]; then
    cp "${file}" "${DST_DIR}/${base}"
    echo "Copied ${base} to ${DST_DIR}"
  fi
done
