#!/bin/zsh
set -euo pipefail

# Unified MAUI iOS runner for simulator or device
# Usage examples:
#   scripts/run-maui-ios.sh --mode sim --project src/HVO.Maui.RoofControllerV4.iPad/HVO.Maui.RoofControllerV4.iPad.csproj --app-id org.hvo.roofcontroller.v4.ipad
#   scripts/run-maui-ios.sh --mode device --udid $HVO_IOS_DEVICE_UDID --project <.csproj> --app-id <bundle id>

SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR}/.."

MODE="sim"
CONFIGURATION="Debug"
FRAMEWORK="net9.0-ios"
RUNTIME_IDENTIFIER_DEVICE="ios-arm64"
UDID="${HVO_IOS_SIM_UDID:-}"  # for --mode sim; override with --udid
PROJECT_PATH="${REPO_ROOT}/src/HVO.iOS/HVO.RoofControllerV4.iPad/HVO.RoofControllerV4.iPad.csproj"
APP_ID="org.hvo.roofcontroller.v4.ipad"
ATTACH_CONSOLE=false

print_usage() {
  cat <<USAGE
Unified MAUI iOS runner

Options:
  --mode sim|device           Run on simulator (sim) or physical device (device). Default: sim
  --configuration Debug|Release  Build configuration. Default: Debug
  --udid <UDID>               Simulator or device UDID. Defaults: HVO_IOS_SIM_UDID (sim), HVO_IOS_DEVICE_UDID (device)
  --project <path.csproj>     Project to build/run. Default: Roof Controller iPad project
  --app-id <bundle id>        App bundle identifier. Default: org.hvo.roofcontroller.v4.ipad
  --console | --no-console    Attach device console (device mode only). Default: --no-console
  -h | --help                 Show this help
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode)
      [[ $# -lt 2 ]] && { echo "Missing value for --mode" >&2; exit 1; }
      MODE="$2"; shift 2 ;;
    --configuration)
      [[ $# -lt 2 ]] && { echo "Missing value for --configuration" >&2; exit 1; }
      CONFIGURATION="$2"; shift 2 ;;
    --udid)
      [[ $# -lt 2 ]] && { echo "Missing value for --udid" >&2; exit 1; }
      UDID="$2"; shift 2 ;;
    --project)
      [[ $# -lt 2 ]] && { echo "Missing value for --project" >&2; exit 1; }
      PROJECT_PATH="$2"; shift 2 ;;
    --app-id)
      [[ $# -lt 2 ]] && { echo "Missing value for --app-id" >&2; exit 1; }
      APP_ID="$2"; shift 2 ;;
    --console)
      ATTACH_CONSOLE=true; shift ;;
    --no-console)
      ATTACH_CONSOLE=false; shift ;;
    -h|--help)
      print_usage; exit 0 ;;
    *)
      echo "Unknown argument: $1" >&2; print_usage >&2; exit 1 ;;
  esac
done

if [[ ! -f "${PROJECT_PATH}" ]]; then
  echo "Project file not found at ${PROJECT_PATH}" >&2
  exit 1
fi

echo "Building ${PROJECT_PATH} (${CONFIGURATION}/${FRAMEWORK})..."
dotnet build "${PROJECT_PATH}" -c "${CONFIGURATION}" -f "${FRAMEWORK}"

if [[ "${MODE}" == "sim" ]]; then
  # Default UDID if none provided
  UDID="${UDID:-${HVO_IOS_SIM_UDID:-}}"
  if [[ -z "${UDID}" ]]; then
    echo "Simulator UDID not provided. Use --udid or set HVO_IOS_SIM_UDID." >&2
    exit 1
  fi

  # Build output path
  APP_OUTPUT_DIR="${REPO_ROOT}/$(dirname "${PROJECT_PATH#${REPO_ROOT}/}")/bin/${CONFIGURATION}/${FRAMEWORK}/iossimulator-arm64"
  APP_BUNDLE_PATH="${APP_OUTPUT_DIR}/$(basename "${PROJECT_PATH%.*}").app"

  echo "Ensuring simulator ${UDID} is booted..."
  if ! xcrun simctl list devices | grep -q "${UDID}"; then
    echo "Simulator with UDID ${UDID} not found. Use xcrun simctl list devices." >&2
    exit 1
  fi

  xcrun simctl boot "${UDID}" >/dev/null 2>&1 || true
  xcrun simctl bootstatus "${UDID}" -b

  echo "Opening Simulator.app for device ${UDID}..."
  open -a Simulator --args -CurrentDeviceUDID "${UDID}" >/dev/null 2>&1 || true

  echo "Uninstalling existing app (if present)..."
  xcrun simctl uninstall "${UDID}" "${APP_ID}" >/dev/null 2>&1 || true

  echo "Installing bundle ${APP_BUNDLE_PATH}..."
  xcrun simctl install "${UDID}" "${APP_BUNDLE_PATH}"

  echo "Launching ${APP_ID} on simulator ${UDID}..."
  xcrun simctl launch "${UDID}" "${APP_ID}" || true
  echo "Simulator launch complete."
else
  # device mode
  UDID="${UDID:-${HVO_IOS_DEVICE_UDID:-}}"
  if [[ -z "${UDID}" ]]; then
    echo "Device UDID is required. Use --udid or set HVO_IOS_DEVICE_UDID." >&2
    exit 1
  fi

  if ! xcrun devicectl --version >/dev/null 2>&1; then
    echo "xcrun devicectl is required (Xcode 15+)." >&2
    exit 1
  fi

  APP_BUNDLE_PATH="${REPO_ROOT}/$(dirname "${PROJECT_PATH#${REPO_ROOT}/}")/bin/${CONFIGURATION}/${FRAMEWORK}/${RUNTIME_IDENTIFIER_DEVICE}/$(basename "${PROJECT_PATH%.*}").app"

  echo "Rebuilding with RuntimeIdentifier for device..."
  dotnet build "${PROJECT_PATH}" -c "${CONFIGURATION}" -f "${FRAMEWORK}" -p:RuntimeIdentifier="${RUNTIME_IDENTIFIER_DEVICE}"

  if [[ ! -d "${APP_BUNDLE_PATH}" ]]; then
    echo "Built app bundle not found at ${APP_BUNDLE_PATH}" >&2
    exit 1
  fi

  echo "Installing app to device ${UDID}..."
  xcrun devicectl device install app --device "${UDID}" "${APP_BUNDLE_PATH}"

  echo "Launching ${APP_ID} on device ${UDID}..."
  if [[ "${ATTACH_CONSOLE}" == true ]]; then
    xcrun devicectl device process launch --terminate-existing --device "${UDID}" "${APP_ID}" --console
  else
    xcrun devicectl device process launch --terminate-existing --device "${UDID}" "${APP_ID}"
  fi

  if [[ "${ATTACH_CONSOLE}" == true ]]; then
    echo "Attached to device console. Press Ctrl+C to detach."
  else
    echo "Device launch complete."
  fi
fi
