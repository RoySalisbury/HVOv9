#!/usr/bin/env bash
set -euo pipefail

# Deploys the Roof Controller V4 container image to a Raspberry Pi over SSH.
#
# Required environment variables:
#   PI_HOST    - Hostname or IP of the Raspberry Pi (e.g. "roofpi.local" or "192.168.1.50")
# Optional environment variables:
#   PI_USER            - SSH username (default: "pi")
#   IMAGE_TAG          - Docker image tag to build/push (default: "hvov9/roof-controller:v4")
#   CONTAINER_NAME     - Container name on the Pi (default: "roof-controller")
#   HOST_PORT          - Host port to expose the HTTP endpoint (default: "8080")
#   EXTRA_DOCKER_ARGS  - Additional arguments appended to docker run (e.g. env vars)
#
# The script will:
#   1. Build the linux/arm64 image locally using docker buildx (with --load).
#   2. Save the image to a temporary tarball and copy it to the Pi via scp.
#   3. Load the image, replace any existing container, and start the new one with the
#      required GPIO/I2C/thermal device mappings.
#
# Example usage:
#   PI_HOST=roofpi.local PI_USER=roy ./scripts/deploy-roofcontroller-rpi.sh
#   PI_HOST=192.168.1.88 HOST_PORT=8081 EXTRA_DOCKER_ARGS="-e ASPNETCORE_ENVIRONMENT=Production" \
#       ./scripts/deploy-roofcontroller-rpi.sh

if [[ -z "${PI_HOST:-}" ]]; then
  echo "PI_HOST environment variable is required" >&2
  exit 1
fi

PI_USER=${PI_USER:-pi}
IMAGE_TAG=${IMAGE_TAG:-hvov9/roof-controller:v4}
CONTAINER_NAME=${CONTAINER_NAME:-roof-controller}
HOST_PORT=${HOST_PORT:-8080}
EXTRA_DOCKER_ARGS=${EXTRA_DOCKER_ARGS:-}

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "${SCRIPT_DIR}/.." && pwd)

# Ensure buildx is available
if ! docker buildx version >/dev/null 2>&1; then
  echo "docker buildx is required but not available. Install Docker Buildx and try again." >&2
  exit 1
fi

# Build the linux/arm64 image locally and load it into the Docker daemon
echo "[build] Building ${IMAGE_TAG} for linux/arm64..."
docker buildx build \
  --platform linux/arm64 \
  -f "${REPO_ROOT}/src/HVO.RoofControllerV4.RPi/Dockerfile" \
  -t "${IMAGE_TAG}" \
  --load \
  "${REPO_ROOT}"

# Save the image to a temporary tarball
TMP_TAR=$(mktemp)
trap 'rm -f "${TMP_TAR}"' EXIT

docker save "${IMAGE_TAG}" -o "${TMP_TAR}"

REMOTE_TAR="/tmp/roof-controller.tar"

# Copy the tarball to the Pi
echo "[deploy] Copying image to ${PI_USER}@${PI_HOST}:${REMOTE_TAR}"
scp "${TMP_TAR}" "${PI_USER}@${PI_HOST}:${REMOTE_TAR}"

# Deploy on the Pi
RUN_COMMAND=$(cat <<EOF
set -euo pipefail

docker load -i "${REMOTE_TAR}"
rm -f "${REMOTE_TAR}"

docker rm -f "${CONTAINER_NAME}" >/dev/null 2>&1 || true

docker run -d \
  --name "${CONTAINER_NAME}" \
  --restart unless-stopped \
  -p "${HOST_PORT}:8080" \
  --device /dev/gpiomem:/dev/gpiomem \
  --device /dev/i2c-1:/dev/i2c-1 \
  --mount type=bind,src=/sys/class/thermal/thermal_zone0/temp,dst=/sys/class/thermal/thermal_zone0/temp,readonly \
  ${EXTRA_DOCKER_ARGS} \
  "${IMAGE_TAG}"

docker ps --filter "name=${CONTAINER_NAME}" --format "table {{.Names}}\t{{.Status}}\t{{.Image}}"
EOF
)

echo "[deploy] Starting container on ${PI_HOST}"
ssh "${PI_USER}@${PI_HOST}" "${RUN_COMMAND}"

echo "[done] Deployment complete. Roof controller is reachable on http://${PI_HOST}:${HOST_PORT}"