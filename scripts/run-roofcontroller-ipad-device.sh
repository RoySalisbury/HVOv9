#!/bin/zsh
set -euo pipefail

# Deprecated wrapper: use scripts/run-maui-ios.sh --mode device

SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR}/.."
PROJECT_PATH="${REPO_ROOT}/src/HVO.Maui.RoofControllerV4.iPad/HVO.Maui.RoofControllerV4.iPad.csproj"
APP_ID="org.hvo.roofcontroller.v4.ipad"

CONFIGURATION="Debug"
DEVICE_UDID="${HVO_ROOF_IPAD_DEVICE_UDID:-}"
ATTACH_CONSOLE=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration) [[ $# -lt 2 ]] && { echo "Missing value for --configuration" >&2; exit 1; }; CONFIGURATION="$2"; shift 2 ;;
    --udid) [[ $# -lt 2 ]] && { echo "Missing value for --udid" >&2; exit 1; }; DEVICE_UDID="$2"; shift 2 ;;
    --console) ATTACH_CONSOLE=true; shift ;;
    --no-console) ATTACH_CONSOLE=false; shift ;;
    -h|--help) echo "Use: scripts/run-maui-ios.sh --mode device [--configuration] [--udid] [--project] [--app-id] [--console]"; exit 0 ;;
    *) shift ;;
  esac
done

exec "${REPO_ROOT}/scripts/run-maui-ios.sh" --mode device --configuration "${CONFIGURATION}" ${DEVICE_UDID:+--udid ${DEVICE_UDID}} --project "${PROJECT_PATH}" --app-id "${APP_ID}" $( [[ "${ATTACH_CONSOLE}" == true ]] && echo "--console" )
