#!/bin/zsh
set -euo pipefail

# Domain-local wrapper: launches the iPad app on a simulator using the shared MAUI iOS runner

SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR}/../../.."
PROJECT_PATH="${REPO_ROOT}/src/HVO.iOS/HVO.RoofControllerV4.iPad/HVO.RoofControllerV4.iPad.csproj"
APP_ID="org.hvo.roofcontroller.v4.ipad"

CONFIGURATION="Debug"
UDID="${HVO_ROOF_IPAD_SIM_UDID:-}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration) [[ $# -lt 2 ]] && { echo "Missing value for --configuration" >&2; exit 1; }; CONFIGURATION="$2"; shift 2 ;;
    --udid) [[ $# -lt 2 ]] && { echo "Missing value for --udid" >&2; exit 1; }; UDID="$2"; shift 2 ;;
    -h|--help) echo "Use: run-roofcontroller-ipad-sim.sh [--configuration] [--udid]"; exit 0 ;;
    *) shift ;;
  esac
done

exec "${REPO_ROOT}/scripts/run-maui-ios.sh" --mode sim --configuration "${CONFIGURATION}" ${UDID:+--udid ${UDID}} --project "${PROJECT_PATH}" --app-id "${APP_ID}"
