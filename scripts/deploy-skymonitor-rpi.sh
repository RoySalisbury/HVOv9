#!/usr/bin/env bash
set -euo pipefail

# Deploy and optionally exercise the SkyMonitor v5 container on a remote Raspberry Pi Docker host.
#
# The script assumes a Docker context named "rpi-remote" has already been configured on the
# development machine. Override any setting via environment variables to fit your environment.
#
# Environment variables:
#   DOCKER_CONTEXT      Docker context to target (default: rpi-remote)
#   IMAGE_TAG           Image tag to build/publish (default: hvov9/skymonitor-v5:latest)
#   CONTAINER_NAME      Name assigned to the running container (default: hvo-skymonitor-v5)
#   DATA_ROOT           Absolute path on the remote host that should back /var/hvo/datastores
#                       (default: /srv/hvo/skymonitor/datastores)
#   RUN_TESTS           When "true", build the Dockerfile tests stage before publishing (default: true)
#   RUN_BENCHMARKS      When "true", run the benchmark smoke job during the tests stage (default: false)
#   START_CONTAINER     When "true", start the service container after the build (default: false)
#   RUN_DURATION        Seconds to keep the container running before it is stopped (default: 60)
#   HOST_HTTP_PORT      Host port mapped to container HTTP (default: 5136)
#   EXTRA_DOCKER_ARGS   Additional arguments appended to docker run (default: none)

REPO_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
DOCKER_CONTEXT=${DOCKER_CONTEXT:-rpi-remote}
IMAGE_TAG=${IMAGE_TAG:-hvov9/skymonitor-v5:latest}
CONTAINER_NAME=${CONTAINER_NAME:-hvo-skymonitor-v5}
DATA_ROOT=${DATA_ROOT:-/srv/hvo/skymonitor/datastores}
RUN_TESTS=${RUN_TESTS:-true}
RUN_BENCHMARKS=${RUN_BENCHMARKS:-false}
START_CONTAINER=${START_CONTAINER:-false}
RUN_DURATION=${RUN_DURATION:-60}
HOST_HTTP_PORT=${HOST_HTTP_PORT:-5136}
EXTRA_DOCKER_ARGS=${EXTRA_DOCKER_ARGS:-}
TAIL_LOGS=${TAIL_LOGS:-true}

DOCKERFILE_PATH="${REPO_ROOT}/src/HVO.SkyMonitorV5.RPi/Dockerfile"
BUILD_VERSION=$(git -C "${REPO_ROOT}" rev-parse --short HEAD 2>/dev/null || echo "dev")

if ! docker --context "${DOCKER_CONTEXT}" info >/dev/null 2>&1; then
  echo "Docker context '${DOCKER_CONTEXT}' is not available. Configure it with 'docker context create' first." >&2
  exit 1
fi

echo "Using Docker context: ${DOCKER_CONTEXT}"
echo "Building image tag:   ${IMAGE_TAG}"
echo "Remote data root:     ${DATA_ROOT}"

export DOCKER_BUILDKIT=1

if [[ "${RUN_TESTS}" == "true" ]]; then
  echo "\n→ Running SkyMonitor test stage on remote host..."
  docker --context "${DOCKER_CONTEXT}" build \
    --target tests \
    --build-arg RUN_BENCHMARKS="${RUN_BENCHMARKS}" \
    -f "${DOCKERFILE_PATH}" \
    "${REPO_ROOT}" >/dev/null
  echo "✓ Test stage completed"
fi

echo "\n→ Building SkyMonitor runtime image..."
docker --context "${DOCKER_CONTEXT}" build \
  --build-arg BUILD_VERSION="${BUILD_VERSION}" \
  -t "${IMAGE_TAG}" \
  -f "${DOCKERFILE_PATH}" \
  "${REPO_ROOT}"

echo "\n→ Ensuring data root directories exist on remote host (${DATA_ROOT})..."
docker --context "${DOCKER_CONTEXT}" run --rm \
  -v "${DATA_ROOT}":/datastore \
  alpine:3.20 \
  sh -c "mkdir -p /datastore/configuration /datastore/telemetry /datastore/catalogs /datastore/logs /datastore/dataprotection && chmod -R 775 /datastore"

echo "\n→ Seeding catalog assets when host directory is empty..."
docker --context "${DOCKER_CONTEXT}" run --rm \
  -v "${DATA_ROOT}/catalogs":/var/hvo/datastores/catalogs \
  --entrypoint sh \
  "${IMAGE_TAG}" \
  -c 'mkdir -p /var/hvo/datastores/catalogs && if [ -z "$(ls -A /var/hvo/datastores/catalogs 2>/dev/null)" ]; then cp -r /app/Data/catalogs/. /var/hvo/datastores/catalogs/; fi'

echo "\n→ Validating volume mappings with a short probe..."
docker --context "${DOCKER_CONTEXT}" run --rm \
  -v "${DATA_ROOT}/configuration":/var/hvo/datastores/configuration \
  -v "${DATA_ROOT}/telemetry":/var/hvo/datastores/telemetry \
  -v "${DATA_ROOT}/catalogs":/var/hvo/datastores/catalogs \
  -v "${DATA_ROOT}/dataprotection":/var/hvo/datastores/dataprotection \
  -v "${DATA_ROOT}/logs":/var/hvo/logs \
  --entrypoint sh \
  "${IMAGE_TAG}" \
  -c 'for dir in configuration telemetry dataprotection; do if [ -w "/var/hvo/datastores/${dir}" ]; then echo "[ok] ${dir} writable"; else echo "[err] ${dir} not writable" >&2; exit 1; fi; done; if [ ! -d /var/hvo/datastores/catalogs ] || [ -z "$(ls -A /var/hvo/datastores/catalogs 2>/dev/null)" ]; then echo "[err] catalogs missing" >&2; exit 1; else echo "[ok] catalogs present"; fi; if [ -w /var/hvo/logs ]; then echo "[ok] logs writable"; else echo "[err] logs not writable" >&2; exit 1; fi'

if [[ "${START_CONTAINER}" != "true" ]]; then
  echo "\nSkyMonitor image is ready. Set START_CONTAINER=true to launch it automatically."
  exit 0
fi

echo "\n→ Starting SkyMonitor container for ${RUN_DURATION}s of runtime observation..."
CONTAINER_ID=$(docker --context "${DOCKER_CONTEXT}" run -d \
  --name "${CONTAINER_NAME}" \
  -p "${HOST_HTTP_PORT}:5136" \
  -v "${DATA_ROOT}/configuration":/var/hvo/datastores/configuration \
  -v "${DATA_ROOT}/telemetry":/var/hvo/datastores/telemetry \
  -v "${DATA_ROOT}/catalogs":/var/hvo/datastores/catalogs \
  -v "${DATA_ROOT}/dataprotection":/var/hvo/datastores/dataprotection \
  -v "${DATA_ROOT}/logs":/var/hvo/logs \
  ${EXTRA_DOCKER_ARGS} \
  "${IMAGE_TAG}")

echo "Container ID: ${CONTAINER_ID}"
cleanup() {
  echo "\n→ Stopping SkyMonitor container..."
  docker --context "${DOCKER_CONTEXT}" stop "${CONTAINER_ID}" >/dev/null || true
  docker --context "${DOCKER_CONTEXT}" rm "${CONTAINER_ID}" >/dev/null || true
}
trap cleanup EXIT INT TERM

if [[ "${TAIL_LOGS}" == "true" ]]; then
  docker --context "${DOCKER_CONTEXT}" logs --tail 200 -f "${CONTAINER_ID}" &
  LOGS_PID=$!
fi

sleep "${RUN_DURATION}" || true

if [[ "${TAIL_LOGS}" == "true" ]]; then
  kill "${LOGS_PID}" >/dev/null 2>&1 || true
fi

cleanup
trap - EXIT INT TERM

echo "\nSkyMonitor container run complete. Review the logs above for simulated performance output."
