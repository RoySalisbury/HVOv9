#!/bin/zsh
# ---------------------------------------------------------------------------------------
# Builds and deploys the HVO Roof Controller V4 iPad MAUI application to a physical iPad.
# ---------------------------------------------------------------------------------------
# Usage: run-roofcontroller-ipad-device.sh [--configuration Debug|Release] [--udid DEVICE_UDID]
#
# Defaults:
#   --configuration Debug
#   --udid read from HVO_ROOF_IPAD_DEVICE_UDID environment variable when not supplied
#
# This mirrors the simulator deployment helper but targets an actual device. The UDID must
# match the connected iPad (e.g., "Roy's iPad"). Ensure the device is trusted, unlocked,
# and that your codesigning assets allow on-device deployment.
# ---------------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
SCRIPT_NAME="${0:t}"
REPO_ROOT="${SCRIPT_DIR}/.."
PROJECT_PATH="${REPO_ROOT}/src/HVO.Maui.RoofControllerV4.iPad/HVO.Maui.RoofControllerV4.iPad.csproj"
FRAMEWORK="net9.0-ios"
RUNTIME_IDENTIFIER="ios-arm64"
CONFIGURATION="Debug"
APP_ID="org.hvo.roofcontroller.v4.ipad"

DEVICE_UDID="${HVO_ROOF_IPAD_DEVICE_UDID:-}"
ATTACH_CONSOLE=false

print_usage() {
    cat <<USAGE
Usage: ${SCRIPT_NAME} [--configuration Debug|Release] [--udid DEVICE_UDID] [--console]

Builds the MAUI iPad project, deploys it to the specified *physical* device, and launches it.

Defaults:
  --configuration ${CONFIGURATION}
  --udid value from HVO_ROOF_IPAD_DEVICE_UDID (currently '${DEVICE_UDID:-unset}')

To discover your device UDID, run:
  xcrun xctrace list devices | grep "Roy's iPad"

Ensure the device is unlocked, trusted, and connected via USB (or network debugging).
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --configuration)
            [[ $# -lt 2 ]] && { echo "Missing value for --configuration" >&2; exit 1; }
            CONFIGURATION="$2"
            shift 2
            ;;
        --udid)
            [[ $# -lt 2 ]] && { echo "Missing value for --udid" >&2; exit 1; }
            DEVICE_UDID="$2"
            shift 2
            ;;
        --console)
            ATTACH_CONSOLE=true
            shift
            ;;
        --no-console)
            ATTACH_CONSOLE=false
            shift
            ;;
        -h|--help)
            print_usage
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            print_usage >&2
            exit 1
            ;;
    esac
done

if [[ -z "${DEVICE_UDID}" ]]; then
    echo "Error: device UDID is required. Use --udid or set HVO_ROOF_IPAD_DEVICE_UDID." >&2
    exit 1
fi

DEVICE_PRESENT=false
if xcrun xctrace list devices | grep -q "${DEVICE_UDID}"; then
    DEVICE_PRESENT=true
fi

if [[ "${DEVICE_PRESENT}" != true ]]; then
    echo "Warning: device with UDID ${DEVICE_UDID} not reported by xctrace. Continuing anyway..." >&2
fi

if ! xcrun devicectl --version >/dev/null 2>&1; then
    echo "Error: xcrun devicectl command is unavailable. Install Xcode 15+ command line tools." >&2
    exit 1
fi

APP_BUNDLE_PATH="${REPO_ROOT}/src/HVO.Maui.RoofControllerV4.iPad/bin/${CONFIGURATION}/${FRAMEWORK}/${RUNTIME_IDENTIFIER}/HVO.Maui.RoofControllerV4.iPad.app"

echo "Building project (${CONFIGURATION}/${FRAMEWORK}/${RUNTIME_IDENTIFIER})..."
dotnet build "${PROJECT_PATH}" \
    -c "${CONFIGURATION}" \
    -f "${FRAMEWORK}" \
    -p:RuntimeIdentifier="${RUNTIME_IDENTIFIER}"

if [[ ! -d "${APP_BUNDLE_PATH}" ]]; then
    echo "Error: built app bundle not found at ${APP_BUNDLE_PATH}" >&2
    exit 1
fi

echo "Installing app to device ${DEVICE_UDID}..."
xcrun devicectl device install app --device "${DEVICE_UDID}" "${APP_BUNDLE_PATH}"

echo "Launching ${APP_ID} on device ${DEVICE_UDID}..."
xcrun devicectl device process launch --terminate-existing --device "${DEVICE_UDID}" "${APP_ID}" $( [[ "${ATTACH_CONSOLE}" == true ]] && echo "--console" )

if [[ "${ATTACH_CONSOLE}" == true ]]; then
    echo "Deployment complete; attached to device console. Press Ctrl+C to detach."
else
    echo "Deployment complete. The device app should now be running in the foreground."
fi
