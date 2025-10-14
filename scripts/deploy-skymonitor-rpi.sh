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
#   EXPORT_ROOT         Absolute path on the remote host that should back /var/hvo/exports
#                       (default: /srv/hvo/skymonitor/exports)
#   RUN_TESTS           When "true", build the Dockerfile tests stage before publishing (default: true)
#   RUN_BENCHMARKS      When "true", run the benchmark smoke job during the tests stage (default: false)
#   START_CONTAINER     When "true", start the service container after the build (default: false)
#   RUN_DURATION        Seconds to keep the container running before it is stopped (default: 60)
#   HOST_HTTP_PORT      Host port mapped to container HTTP (default: 5136)
#   EXTRA_DOCKER_ARGS   Additional arguments appended to docker run (default: none)

REPO_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
if [[ -z "${DOCKER_CONTEXT:-}" ]]; then
  echo "DOCKER_CONTEXT must be set (e.g. rpi-remote)" >&2
  exit 1
fi

# Using defaults here would mask configuration issues (e.g. wrong data root or log handling),
# so require callers to be explicit for scripts invoked from automation.
: "${IMAGE_TAG:?Set IMAGE_TAG to the target tag, e.g. hvov9/skymonitor-v5:latest}"
: "${CONTAINER_NAME:?Set CONTAINER_NAME to the container name, e.g. hvo-skymonitor-v5}"
: "${DATA_ROOT:?Set DATA_ROOT to the remote datastore root, e.g. /srv/hvo/skymonitor/datastores}"
: "${EXPORT_ROOT:?Set EXPORT_ROOT to the remote export root, e.g. /srv/hvo/skymonitor/exports}"
: "${RUN_TESTS:?Set RUN_TESTS to true/false explicitly}"
: "${RUN_BENCHMARKS:?Set RUN_BENCHMARKS to true/false explicitly}"
: "${START_CONTAINER:?Set START_CONTAINER to true/false explicitly}"
: "${RUN_DURATION:?Set RUN_DURATION to desired seconds (e.g. 60)}"
: "${HOST_HTTP_PORT:?Set HOST_HTTP_PORT to the desired port (e.g. 5136)}"
: "${TAIL_LOGS:?Set TAIL_LOGS to true/false explicitly}"

SKYMONITOR_RUNTIME=${SKYMONITOR_RUNTIME:-}
EXTRA_DOCKER_ARGS=${EXTRA_DOCKER_ARGS:-}

echo "Using Docker context: ${DOCKER_CONTEXT}"
echo "Building image tag:   ${IMAGE_TAG}"
echo "Remote data root:     ${DATA_ROOT}"
echo "Remote export root:   ${EXPORT_ROOT}"

DOCKERFILE_PATH="${REPO_ROOT}/src/HVO.SkyMonitorV5.RPi/Dockerfile"
BUILD_VERSION=$(git -C "${REPO_ROOT}" rev-parse --short HEAD 2>/dev/null || echo "dev")

if ! docker --context "${DOCKER_CONTEXT}" info >/dev/null 2>&1; then
  echo "Docker context '${DOCKER_CONTEXT}' is not available. Configure it with 'docker context create' first." >&2
  exit 1
fi

echo "Using Docker context: ${DOCKER_CONTEXT}"
echo "Building image tag:   ${IMAGE_TAG}"
echo "Remote data root:     ${DATA_ROOT}"
echo "Remote export root:   ${EXPORT_ROOT}"

HOST_ARCH=$(docker --context "${DOCKER_CONTEXT}" info --format '{{.Architecture}}')
if [[ -z "${SKYMONITOR_RUNTIME}" ]]; then
  case "${HOST_ARCH}" in
    aarch64)
      SKYMONITOR_RUNTIME="linux-arm64"
      ;;
    armv7l|armhf)
      SKYMONITOR_RUNTIME="linux-arm"
      ;;
    x86_64)
      SKYMONITOR_RUNTIME="linux-x64"
      ;;
    *)
      echo "Unsupported Docker host architecture '${HOST_ARCH}'. Set SKYMONITOR_RUNTIME explicitly." >&2
      exit 1
      ;;
  esac
fi

echo "Docker host arch:    ${HOST_ARCH}"
echo "Target runtime:      ${SKYMONITOR_RUNTIME}"

export DOCKER_BUILDKIT=1

if [[ "${RUN_TESTS}" == "true" ]]; then
  echo "\n→ Running SkyMonitor test stage on remote host..."
  docker --context "${DOCKER_CONTEXT}" build \
    --target tests \
    --build-arg RUN_BENCHMARKS="${RUN_BENCHMARKS}" \
    --build-arg SKYMONITOR_RUNTIME="${SKYMONITOR_RUNTIME}" \
    -f "${DOCKERFILE_PATH}" \
    "${REPO_ROOT}" >/dev/null
  echo "✓ Test stage completed"
fi

echo "\n→ Building SkyMonitor runtime image..."
docker --context "${DOCKER_CONTEXT}" build \
  --build-arg BUILD_VERSION="${BUILD_VERSION}" \
  --build-arg SKYMONITOR_RUNTIME="${SKYMONITOR_RUNTIME}" \
  -t "${IMAGE_TAG}" \
  -f "${DOCKERFILE_PATH}" \
  "${REPO_ROOT}"

echo "\n→ Ensuring data root directories exist on remote host (${DATA_ROOT})..."
docker --context "${DOCKER_CONTEXT}" run --rm \
  -v "${DATA_ROOT}":/datastore \
  alpine:3.20 \
  sh -c "mkdir -p /datastore/configuration /datastore/telemetry /datastore/catalogs /datastore/logs /datastore/dataprotection && chmod -R 775 /datastore"

echo "\n→ Ensuring export root directories exist on remote host (${EXPORT_ROOT})..."
docker --context "${DOCKER_CONTEXT}" run --rm \
  -v "${EXPORT_ROOT}":/exports \
  alpine:3.20 \
  sh -c "mkdir -p /exports/raw /exports/processed && chmod -R 775 /exports"

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
  -v "${EXPORT_ROOT}/raw":/var/hvo/exports/raw \
  -v "${EXPORT_ROOT}/processed":/var/hvo/exports/processed \
  --entrypoint sh \
  "${IMAGE_TAG}" \
  -c 'for dir in configuration telemetry dataprotection; do if [ -w "/var/hvo/datastores/${dir}" ]; then echo "[ok] ${dir} writable"; else echo "[err] ${dir} not writable" >&2; exit 1; fi; done; if [ ! -d /var/hvo/datastores/catalogs ] || [ -z "$(ls -A /var/hvo/datastores/catalogs 2>/dev/null)" ]; then echo "[err] catalogs missing" >&2; exit 1; else echo "[ok] catalogs present"; fi; if [ -w /var/hvo/logs ]; then echo "[ok] logs writable"; else echo "[err] logs not writable" >&2; exit 1; fi; for dir in raw processed; do if [ -w "/var/hvo/exports/${dir}" ]; then echo "[ok] exports/${dir} writable"; else echo "[err] exports/${dir} not writable" >&2; exit 1; fi; done'

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
  -v "${EXPORT_ROOT}/raw":/var/hvo/exports/raw \
  -v "${EXPORT_ROOT}/processed":/var/hvo/exports/processed \
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
