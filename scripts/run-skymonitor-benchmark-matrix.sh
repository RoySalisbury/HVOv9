#!/usr/bin/env bash
# Run the SkyMonitor v5 container through a benchmark matrix on the remote Pi.
# The script assumes docker context access to the Pi and loops through camera/queue
# combinations, capturing telemetry/log artifacts per scenario.

set -euo pipefail

REPO_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
DOCKER_CONTEXT=${DOCKER_CONTEXT:-rpi-remote}
DATA_ROOT=${DATA_ROOT:-/srv/hvo/skymonitor/datastores}
IMAGE_TAG=${IMAGE_TAG:-hvov9/skymonitor-v5:latest}
RUN_DURATION=${RUN_DURATION:-1800}
HOST_HTTP_PORT=${HOST_HTTP_PORT:-5136}
CONTAINER_PREFIX=${CONTAINER_PREFIX:-hvo-skymonitor-v5-bench}
SCENARIO_FILTER=${SCENARIO_FILTER:-}

# Logging overrides to mute Debug/Trace chatter during the benchmark runs.
LOG_OVERRIDE_ARGS=(
  "-e" "Logging__LogLevel__Default=Information"
  "-e" "Logging__LogLevel__HVO.SkyMonitorV5.RPi=Information"
  "-e" "Logging__LogLevel__HVO.SkyMonitorV5.RPi.HostedServices.AllSkyCaptureService=Information"
  "-e" "Logging__LogLevel__HVO.SkyMonitorV5.RPi.Pipeline.FrameFilterPipeline=Information"
  "-e" "Logging__LogLevel__HVO.SkyMonitorV5.RPi.Pipeline.RollingFrameStacker=Information"
  "-e" "Logging__LogLevel__Microsoft=Warning"
)

SCENARIO_LABELS=(
  "mono-bg-on"
  "color-bg-on"
  "mono-bg-off"
  "color-bg-off"
)
SCENARIO_COLOR_MODES=(
  "Monochrome"
  "Color"
  "Monochrome"
  "Color"
)
SCENARIO_ADAPTER_TYPES=(
  "Mock"
  "MockColor"
  "Mock"
  "MockColor"
)
SCENARIO_BG_ENABLED=(
  "1"
  "1"
  "0"
  "0"
)
SCENARIO_BG_ADAPTIVE_ENABLED=(
  "1"
  "1"
  "0"
  "0"
)

run_sql() {
  local sql=$1
  docker --context "${DOCKER_CONTEXT}" run --rm \
    -v "${DATA_ROOT}/configuration":/config \
    alpine:3.20 sh -c "set -e; apk add --no-cache sqlite >/dev/null; sqlite3 /config/sm-config.db \"${sql}\""
}

archive_telemetry() {
  local label=$1
  docker --context "${DOCKER_CONTEXT}" run --rm \
    -v "${DATA_ROOT}/telemetry":/telemetry \
    alpine:3.20 sh -c "set -e; mkdir -p /telemetry/archive; if [ -f /telemetry/sm-telemetry.db ]; then stamp=\$(date -u +%Y%m%d-%H%M%S); dest=/telemetry/archive/sm-telemetry_${label}_\${stamp}.db; cp /telemetry/sm-telemetry.db \${dest}; rm /telemetry/sm-telemetry.db; fi"
}

archive_logs() {
  local label=$1
  docker --context "${DOCKER_CONTEXT}" run --rm \
    -v "${DATA_ROOT}/logs":/logs \
  alpine:3.20 sh -c "set -e; mkdir -p /logs/archive; found=false; for file in /logs/skymonitor.log*; do if [ -f \"\$file\" ]; then found=true; break; fi; done; if [ \"\$found\" = true ]; then stamp=\$(date -u +%Y%m%d-%H%M%S); dest=/logs/archive/${label}_\${stamp}; mkdir -p \"\$dest\"; for file in /logs/skymonitor.log*; do [ -f \"\$file\" ] || continue; mv \"\$file\" \"\$dest/\"; done; fi"
}

update_configuration() {
  local color_mode=$1
  local adapter_type=$2
  local bg_enabled=$3
  local bg_adaptive_enabled=$4

  local display_name="Mock ASI174MM"
  local camera_key="MockASI174MM"
  local adapter_name="MockCameraAdapter"
  if [ "${color_mode}" = "Color" ]; then
    display_name="Mock ASI174MC"
    camera_key="MockASI174MC"
    adapter_name="MockColorCameraAdapter"
  fi

  run_sql "BEGIN TRANSACTION; \
update camera_catalog_camera set color_mode='${color_mode}', adapter_name='${adapter_name}', display_name='${display_name}', key='${camera_key}' where id=1; \
update camera_adapter_config set adapter_type='${adapter_type}' where id=1; \
update camera_pipeline_config set bg_enabled=${bg_enabled}, bg_adaptive_enabled=${bg_adaptive_enabled}, pacing_enabled=1 where id=1; \
COMMIT;"
}

ensure_image_ready() {
  echo "→ Building SkyMonitor image (no container run)"
  DOCKER_CONTEXT="${DOCKER_CONTEXT}" \
  IMAGE_TAG="${IMAGE_TAG}" \
  RUN_TESTS=false \
  RUN_BENCHMARKS=false \
  START_CONTAINER=false \
  bash "${REPO_ROOT}/src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/deploy-skymonitor-rpi.sh"
}

run_container_scenario() {
  local label=$1
  local container_name="${CONTAINER_PREFIX}-${label}"

  echo "→ Launching scenario '${label}' (container ${container_name})"
  local container_id
  container_id=$(docker --context "${DOCKER_CONTEXT}" run -d \
    --name "${container_name}" \
    -p "${HOST_HTTP_PORT}:5136" \
    -v "${DATA_ROOT}/configuration":/var/hvo/datastores/configuration \
    -v "${DATA_ROOT}/telemetry":/var/hvo/datastores/telemetry \
    -v "${DATA_ROOT}/catalogs":/var/hvo/datastores/catalogs \
    -v "${DATA_ROOT}/dataprotection":/var/hvo/datastores/dataprotection \
    -v "${DATA_ROOT}/logs":/var/hvo/logs \
    "${LOG_OVERRIDE_ARGS[@]}" \
    "${IMAGE_TAG}")
  echo "   Container id: ${container_id}"

  echo "   Running for ${RUN_DURATION}s..."
  sleep "${RUN_DURATION}" || true

  local status
  status=$(docker --context "${DOCKER_CONTEXT}" inspect -f '{{.State.Status}}' "${container_id}")
  local exit_code
  exit_code=$(docker --context "${DOCKER_CONTEXT}" inspect -f '{{.State.ExitCode}}' "${container_id}")

  if [ "${status}" = "running" ]; then
    docker --context "${DOCKER_CONTEXT}" stop "${container_id}" >/dev/null
    status="stopped"
  fi

  docker --context "${DOCKER_CONTEXT}" rm "${container_id}" >/dev/null || true
  echo "   Scenario '${label}' finished with status ${status}, exit code ${exit_code}."
}

restore_baseline_configuration() {
  echo "→ Restoring baseline (monochrome, background queue enabled)"
  update_configuration "Monochrome" "Mock" "1" "1"
}

main() {
  ensure_image_ready

  local selected_labels=()
  if [ -n "${SCENARIO_FILTER}" ]; then
    IFS=',' read -r -a selected_labels <<< "${SCENARIO_FILTER}"
  fi

  local total=${#SCENARIO_LABELS[@]}
  for ((i=0; i<total; i++)); do
    local label=${SCENARIO_LABELS[$i]}
    if [ ${#selected_labels[@]} -ne 0 ]; then
      local match=false
      for selected in "${selected_labels[@]}"; do
        if [ "${label}" = "${selected}" ]; then
          match=true
          break
        fi
      done
      if [ "${match}" != true ]; then
        continue
      fi
    fi
    local color_mode=${SCENARIO_COLOR_MODES[$i]}
    local adapter_type=${SCENARIO_ADAPTER_TYPES[$i]}
    local bg_enabled=${SCENARIO_BG_ENABLED[$i]}
    local bg_adaptive=${SCENARIO_BG_ADAPTIVE_ENABLED[$i]}

    echo "\n=== Scenario ${i+1}/${total}: ${label} ==="
    update_configuration "${color_mode}" "${adapter_type}" "${bg_enabled}" "${bg_adaptive}"
    archive_telemetry "pre-${label}"
    archive_logs "pre-${label}"
    run_container_scenario "${label}"
    archive_telemetry "${label}"
    archive_logs "${label}"
  done

  restore_baseline_configuration
  echo "→ Benchmark matrix complete"
}

main "$@"
