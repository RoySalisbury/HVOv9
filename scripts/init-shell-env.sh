#!/usr/bin/env bash
# Shell initialization script for HVO dev container
# This script is sourced automatically when starting new shells
# to ensure environment variables are always available

# Find the repository root - try multiple approaches for robustness
if [[ -n "${BASH_SOURCE[0]:-}" ]]; then
  REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
elif [[ -d "/workspaces/HVOv9" ]]; then
  REPO_ROOT="/workspaces/HVOv9"
else
  # Fallback: search for the repo in common locations
  for potential_root in "/workspaces/HVOv9" "${PWD}" "${HOME}/workspace/HVOv9"; do
    if [[ -f "${potential_root}/.devcontainer/devcontainer.local.env" ]]; then
      REPO_ROOT="${potential_root}"
      break
    fi
  done
fi

LOCAL_ENV_FILE="${REPO_ROOT}/.devcontainer/devcontainer.local.env"

# Only load if environment variables aren't already set
if [[ -z "${HVO_SECRET__SSH__PRIVATE_KEY_B64:-}" ]] && [[ -f "${LOCAL_ENV_FILE}" ]]; then
  # Load environment variables quietly
  set -a  # Export all variables automatically
  source "${LOCAL_ENV_FILE}" 2>/dev/null || true
  set +a  # Stop auto-exporting
fi