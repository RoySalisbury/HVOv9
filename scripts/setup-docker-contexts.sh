#!/usr/bin/env bash
set -euo pipefail

# Ensures the Docker CLI contexts required for HVO development exist.
# These contexts are created with descriptions matching the host environments
# spelled out in the deployment docs. Creation is idempotent; existing contexts
# are left untouched so any local overrides persist.

ensure_context() {
  local name="$1"
  local description="$2"
  local docker_host="$3"

  if docker context inspect "${name}" >/dev/null 2>&1; then
    echo "[docker-contexts] Context '${name}' already present. Skipping."
    return
  fi

  echo "[docker-contexts] Creating context '${name}' (${description})"
  docker context create "${name}" \
    --description "${description}" \
    --docker "host=${docker_host}" >/dev/null
}

ensure_context "hvo-local-mac" "Local M2 Desktop Docker for Development" "unix:///Users/roys/.docker/run/docker.sock"
ensure_context "hvo-proxmox-home" "Docker Desktop" "ssh://roys@192.168.2.104"
ensure_context "rpi-remote" "Remote engine on Raspberry Pi" "ssh://roys@192.168.2.3"

echo "[docker-contexts] Context setup complete. Use 'docker context use <name>' to switch targets."
