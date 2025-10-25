#!/bin/zsh
set -euo pipefail

# Deprecated shim: moved to src/HVO.iOS/HVO.RoofControllerV4.iPad/run-roofcontroller-ipad-device.sh
SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR}/.."
NEW_SCRIPT="${REPO_ROOT}/src/HVO.iOS/scripts/run-roofcontroller-ipad-device.sh"
echo "[shim] Delegating to ${NEW_SCRIPT}" >&2
exec "${NEW_SCRIPT}" "$@"
