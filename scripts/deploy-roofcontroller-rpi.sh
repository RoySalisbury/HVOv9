#!/usr/bin/env bash
set -euo pipefail

# Deploys the Roof Controller V4 container image to a Raspberry Pi via a Docker context.
#
# Required environment variables:
#   PI_HOST            - Hostname or IP of the Raspberry Pi (e.g. "roofpi.local" or "192.168.1.50")
# Optional environment variables:
#   DOCKER_CONTEXT     - Docker context targeting the Pi daemon (default: "rpi-remote")
#   IMAGE_TAG          - Docker image tag to build/push (default: "hvov9/roof-controller:v4")
#   CONTAINER_NAME     - Container name on the Pi (default: "roof-controller")
#   HOST_PORT          - Host port to expose the HTTP endpoint (default: "8080")
#   EXTRA_DOCKER_ARGS  - Additional arguments appended to docker run (e.g. env vars)
#
# The script will:
#   1. Build the linux/arm64 image locally using docker buildx (with --load).
#   2. Stream the image into the specified Docker context (typically pointing at the Pi).
#   3. Replace any existing container and start the new one with the required
#      GPIO/I2C/thermal device mappings.
#
# Example usage:
#   PI_HOST=roofpi.local DOCKER_CONTEXT=rpi-remote ./scripts/deploy-roofcontroller-rpi.sh
#   PI_HOST=192.168.1.88 HOST_PORT=8081 EXTRA_DOCKER_ARGS="-e ASPNETCORE_ENVIRONMENT=Production" \
#       ./scripts/deploy-roofcontroller-rpi.sh

# PI_HOST is still required so we can report the deployment target in logs/output.
if [[ -z "${PI_HOST:-}" ]]; then
  echo "PI_HOST environment variable is required" >&2
  exit 1
fi

DOCKER_CONTEXT=${DOCKER_CONTEXT:-rpi-remote}
IMAGE_TAG=${IMAGE_TAG:-hvov9/roof-controller:v4}
CONTAINER_NAME=${CONTAINER_NAME:-roof-controller}
HOST_PORT=${HOST_PORT:-8080}
EXTRA_DOCKER_ARGS=${EXTRA_DOCKER_ARGS:-}

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "${SCRIPT_DIR}/.." && pwd)

# Ensure the requested Docker context is available before doing any heavy lifting.
if ! docker --context "${DOCKER_CONTEXT}" info >/dev/null 2>&1; then
  echo "Docker context '${DOCKER_CONTEXT}' is not available. Configure it first (e.g. docker context use ${DOCKER_CONTEXT})." >&2
  exit 1
fi

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

echo "[deploy] Loading image into Docker context '${DOCKER_CONTEXT}'"
docker --context "${DOCKER_CONTEXT}" load < "${TMP_TAR}"

echo "[deploy] Removing any existing container named ${CONTAINER_NAME}"
docker --context "${DOCKER_CONTEXT}" rm -f "${CONTAINER_NAME}" >/dev/null 2>&1 || true

echo "[deploy] Starting container on ${PI_HOST}"

# Build the docker run command as an array so EXTRA_DOCKER_ARGS can be injected safely.
run_cmd=(
  docker --context "${DOCKER_CONTEXT}" run -d
  --name "${CONTAINER_NAME}"
  --restart unless-stopped
  -p "${HOST_PORT}:8080"
  --device /dev/gpiomem:/dev/gpiomem
  --device /dev/i2c-1:/dev/i2c-1
  --mount type=bind,src=/sys/class/thermal/thermal_zone0/temp,dst=/sys/class/thermal/thermal_zone0/temp,readonly
)

if [[ -n "${EXTRA_DOCKER_ARGS}" ]]; then
  # shellcheck disable=SC2206 # word splitting is intentional so callers can pass multiple args
  extra_args=(${EXTRA_DOCKER_ARGS})
  run_cmd+=("${extra_args[@]}")
fi

run_cmd+=("${IMAGE_TAG}")

"${run_cmd[@]}"

echo "[deploy] Container status"
docker --context "${DOCKER_CONTEXT}" ps --filter "name=${CONTAINER_NAME}" --format "table {{.Names}}\t{{.Status}}\t{{.Image}}"

echo "[done] Deployment complete. Roof controller is reachable on http://${PI_HOST}:${HOST_PORT}"