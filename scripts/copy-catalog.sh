#!/usr/bin/env bash
set -euo pipefail

# Shim: this script moved to src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/copy-catalog.sh
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
NEW_SCRIPT="${REPO_ROOT}/src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/copy-catalog.sh"
echo "[shim] Delegating to ${NEW_SCRIPT}" >&2
exec "${NEW_SCRIPT}" "$@"
