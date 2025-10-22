#!/usr/bin/env bash
# Load environment variables from devcontainer.local.env file
# Usage: source scripts/load-local-env.sh

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCAL_ENV_FILE="${REPO_ROOT}/.devcontainer/devcontainer.local.env"

if [[ -f "${LOCAL_ENV_FILE}" ]]; then
  echo "[load-local-env] Loading environment variables from ${LOCAL_ENV_FILE}"
  
  # Count non-empty, non-comment lines
  local_count=$(grep -v '^[[:space:]]*#' "${LOCAL_ENV_FILE}" | grep -v '^[[:space:]]*$' | wc -l)
  
  set -a  # Export all variables automatically
  source "${LOCAL_ENV_FILE}"
  set +a  # Stop auto-exporting
  
  echo "[load-local-env] Loaded ${local_count} environment variables"
  
  # Test key variables
  if [[ -n "${HVO_SECRET__SSH__PRIVATE_KEY_B64:-}" ]]; then
    echo "[load-local-env] ✅ SSH private key loaded (${#HVO_SECRET__SSH__PRIVATE_KEY_B64} chars)"
  else
    echo "[load-local-env] ⚠️  SSH private key not set"
  fi
  
  if [[ -n "${HVO_SECRET__SSH__PUBLIC_KEY_B64:-}" ]]; then
    echo "[load-local-env] ✅ SSH public key loaded (${#HVO_SECRET__SSH__PUBLIC_KEY_B64} chars)"
  else
    echo "[load-local-env] ⚠️  SSH public key not set"
  fi
  
else
  echo "[load-local-env] ❌ Local environment file not found: ${LOCAL_ENV_FILE}"
  echo "[load-local-env] Run: cp .devcontainer/devcontainer.local.env.example .devcontainer/devcontainer.local.env"
  return 1
fi