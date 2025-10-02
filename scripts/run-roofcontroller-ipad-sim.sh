#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR}/.."
PROJECT_PATH="${REPO_ROOT}/src/HVO.Maui.RoofControllerV4.iPad/HVO.Maui.RoofControllerV4.iPad.csproj"
FRAMEWORK="net9.0-ios"
CONFIGURATION="Debug"
UDID_DEFAULT="F878E277-60EC-43CF-90EC-B1C9050549E6"
APP_ID="org.hvo.roofcontroller.v4.ipad"

print_usage() {
    cat <<'EOF'
Usage: run-roofcontroller-ipad-sim.sh [--configuration Debug|Release] [--udid SIMULATOR_UDID]

Builds the MAUI iPad project, installs it to the specified simulator, and launches it.
Defaults:
  --configuration Debug
  --udid F878E277-60EC-43CF-90EC-B1C9050549E6 (iPad (A16))

The UDID can also be provided via the HVO_ROOF_IPAD_SIM_UDID environment variable.
EOF
}

CONFIGURATION_ARG_SET=false
UDID="${HVO_ROOF_IPAD_SIM_UDID:-$UDID_DEFAULT}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --configuration)
            [[ $# -lt 2 ]] && { echo "Missing value for --configuration" >&2; exit 1; }
            CONFIGURATION="$2"
            CONFIGURATION_ARG_SET=true
            shift 2
            ;;
        --udid)
            [[ $# -lt 2 ]] && { echo "Missing value for --udid" >&2; exit 1; }
            UDID="$2"
            shift 2
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

if [[ ! -f "$PROJECT_PATH" ]]; then
    echo "Project file not found at $PROJECT_PATH" >&2
    exit 1
fi

APP_OUTPUT_DIR="${REPO_ROOT}/src/HVO.Maui.RoofControllerV4.iPad/bin/${CONFIGURATION}/${FRAMEWORK}/iossimulator-arm64"
APP_BUNDLE_PATH="${APP_OUTPUT_DIR}/HVO.Maui.RoofControllerV4.iPad.app"

if [[ ! -d "$APP_OUTPUT_DIR" ]]; then
    mkdir -p "$APP_OUTPUT_DIR"
fi

echo "Building $PROJECT_PATH ($CONFIGURATION/$FRAMEWORK)..."
dotnet build "$PROJECT_PATH" -c "$CONFIGURATION" -f "$FRAMEWORK"

echo "Ensuring simulator ${UDID} is booted..."
if ! xcrun simctl list devices | grep -q "$UDID"; then
    echo "Simulator with UDID $UDID not found. Use xcrun simctl list devices to locate the desired simulator." >&2
    exit 1
fi

xcrun simctl boot "$UDID" >/dev/null 2>&1 || true
xcrun simctl bootstatus "$UDID" -b

echo "Opening Simulator.app for device ${UDID}..."
open -a Simulator --args -CurrentDeviceUDID "$UDID" >/dev/null 2>&1 || true

echo "Uninstalling existing app (if present)..."
xcrun simctl uninstall "$UDID" "$APP_ID" >/dev/null 2>&1 || true

echo "Installing bundle $APP_BUNDLE_PATH..."
xcrun simctl install "$UDID" "$APP_BUNDLE_PATH"

echo "Launching $APP_ID on simulator $UDID..."
LAUNCH_OUTPUT=$(xcrun simctl launch "$UDID" "$APP_ID")
echo "$LAUNCH_OUTPUT"

echo "Simulator launch complete. The app should now be running."
