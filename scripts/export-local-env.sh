#!/usr/bin/env bash
# Export environment variables for dev container from devcontainer.local.env file
# Source this file in your shell before launching VS Code to make secrets available to dev containers
#
# Usage:
#   source scripts/export-local-env.sh
#   code .

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCAL_ENV_FILE="${REPO_ROOT}/.devcontainer/devcontainer.local.env"

if [[ ! -f "${LOCAL_ENV_FILE}" ]]; then
  echo "[export-local-env] ERROR: Local environment file not found: ${LOCAL_ENV_FILE}"
  echo "[export-local-env] Run: bash scripts/setup-local-dev-secrets.sh"
  return 1
fi

echo "[export-local-env] Exporting environment variables from ${LOCAL_ENV_FILE}"

# Export all non-comment, non-empty lines as environment variables
while IFS= read -r line; do
  # Skip comments and empty lines
  if [[ "${line}" =~ ^[[:space:]]*# ]] || [[ -z "${line// }" ]]; then
    continue
  fi
  
  # Export the variable
  if [[ "${line}" =~ ^[A-Za-z_][A-Za-z0-9_]*= ]]; then
    export "${line?}"
    echo "[export-local-env] Exported: ${line%%=*}"
  fi
done < "${LOCAL_ENV_FILE}"

echo "[export-local-env] Environment variables exported successfully."
echo "[export-local-env] You can now launch VS Code: code ."
echo "[export-local-env] The dev container will have access to these secrets via remoteEnv."