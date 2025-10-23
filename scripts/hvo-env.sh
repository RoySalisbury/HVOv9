#!/usr/bin/env bash
# Note: Do NOT enable set -euo globally at top-level when this script is sourced from .bashrc.
# We'll temporarily enable strict mode during execution and restore the caller's shell options.

# Detect if this script is being sourced (common when called from .bashrc)
__HVO_ENV_SOURCED=0
if [[ "${BASH_SOURCE[0]}" != "$0" ]]; then
  __HVO_ENV_SOURCED=1
fi

# Save caller shell options so we can restore after running
__HVO_ENV_ORIG_SET_OPTS=""
if (( __HVO_ENV_SOURCED )); then
  __HVO_ENV_ORIG_SET_OPTS="$(set +o)"
fi

# HVO environment helper
# Usage:
#   ./scripts/hvo-env.sh export   # export vars to current shell (use: source scripts/hvo-env.sh export)
#   ./scripts/hvo-env.sh load     # load vars (set -a; source file; set +a) with a short report
#   ./scripts/hvo-env.sh auto     # auto-load when variables are not already set (for shell startup)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Resolve repo root robustly across local shells, dev containers, and Codespaces
resolve_repo_root() {
  local candidates=(
    "${SCRIPT_DIR}/.."
    "/workspaces/HVOv9"
    "${PWD}"
    "${HOME}/workspace/HVOv9"
  )
  for c in "${candidates[@]}"; do
    if [[ -d "${c}" && -f "${c}/.devcontainer/devcontainer.local.env" ]]; then
      echo "$(cd "${c}" && pwd)"
      return 0
    fi
  done
  # Fallback to the script dir's parent
  echo "$(cd "${SCRIPT_DIR}/.." && pwd)"
}

REPO_ROOT="$(resolve_repo_root)"
LOCAL_ENV_FILE="${REPO_ROOT}/.devcontainer/devcontainer.local.env"

print_report() {
  # Count non-empty, non-comment lines
  local local_count=0
  if [[ -f "${LOCAL_ENV_FILE}" ]]; then
    local_count=$(grep -v '^[[:space:]]*#' "${LOCAL_ENV_FILE}" | grep -v '^[[:space:]]*$' | wc -l | tr -d ' ')
  fi
  echo "[hvo-env] Using: ${LOCAL_ENV_FILE} (${local_count} entries)"
  if [[ -n "${HVO_SECRET__SSH__PRIVATE_KEY_B64:-}" ]]; then
    echo "[hvo-env] ✅ SSH private key loaded (${#HVO_SECRET__SSH__PRIVATE_KEY_B64} chars)"
  else
    echo "[hvo-env] ⚠️  SSH private key not set"
  fi
  if [[ -n "${HVO_SECRET__SSH__PUBLIC_KEY_B64:-}" ]]; then
    echo "[hvo-env] ✅ SSH public key loaded (${#HVO_SECRET__SSH__PUBLIC_KEY_B64} chars)"
  else
    echo "[hvo-env] ⚠️  SSH public key not set"
  fi
}

cmd_export() {
  if [[ ! -f "${LOCAL_ENV_FILE}" ]]; then
    echo "[hvo-env] ERROR: ${LOCAL_ENV_FILE} not found."
    echo "[hvo-env] Create it from example or run scripts/setup-local-dev-secrets.sh"
    return 1
  fi
  while IFS= read -r line; do
    # Skip comments and blank lines
    if [[ "${line}" =~ ^[[:space:]]*# ]] || [[ -z "${line// }" ]]; then
      continue
    fi
    # Only process KEY=VALUE lines, capture key as everything before first '=' and value as the rest
    if [[ "${line}" =~ ^([A-Za-z_][A-Za-z0-9_]*)=(.*)$ ]]; then
      key="${BASH_REMATCH[1]}"
      val="${BASH_REMATCH[2]}"
      # Trim optional surrounding quotes in value
      if [[ "${val}" =~ ^\"(.*)\"$ ]]; then val="${BASH_REMATCH[1]}"; fi
      if [[ "${val}" =~ ^'(.*)'$ ]]; then val="${BASH_REMATCH[1]}"; fi
      export "${key}=${val}"
      echo "[hvo-env] export: ${key}"
    fi
  done < "${LOCAL_ENV_FILE}"
  echo "[hvo-env] export complete."
}

cmd_load() {
  if [[ ! -f "${LOCAL_ENV_FILE}" ]]; then
    echo "[hvo-env] ❌ Local env file not found: ${LOCAL_ENV_FILE}"
    echo "[hvo-env] Run: cp .devcontainer/devcontainer.local.env.example .devcontainer/devcontainer.local.env"
    return 1
  fi
  # Parse the env file safely without executing it (supports spaces/semicolons)
  while IFS= read -r line; do
    # Skip comments and blank lines
    if [[ "${line}" =~ ^[[:space:]]*# ]] || [[ -z "${line// }" ]]; then
      continue
    fi
    if [[ "${line}" =~ ^([A-Za-z_][A-Za-z0-9_]*)=(.*)$ ]]; then
      key="${BASH_REMATCH[1]}"
      val="${BASH_REMATCH[2]}"
      # Trim optional surrounding quotes in value
      if [[ "${val}" =~ ^\"(.*)\"$ ]]; then val="${BASH_REMATCH[1]}"; fi
      if [[ "${val}" =~ ^'(.*)'$ ]]; then val="${BASH_REMATCH[1]}"; fi
      export "${key}=${val}"
    fi
  done < "${LOCAL_ENV_FILE}"
  print_report
}

cmd_auto() {
  if [[ -z "${HVO_SECRET__SSH__PRIVATE_KEY_B64:-}" && -f "${LOCAL_ENV_FILE}" ]]; then
    while IFS= read -r line; do
      if [[ "${line}" =~ ^[[:space:]]*# ]] || [[ -z "${line// }" ]]; then
        continue
      fi
      if [[ "${line}" =~ ^([A-Za-z_][A-Za-z0-9_]*)=(.*)$ ]]; then
        key="${BASH_REMATCH[1]}"
        val="${BASH_REMATCH[2]}"
        if [[ "${val}" =~ ^\"(.*)\"$ ]]; then val="${BASH_REMATCH[1]}"; fi
        if [[ "${val}" =~ ^'(.*)'$ ]]; then val="${BASH_REMATCH[1]}"; fi
        export "${key}=${val}"
      fi
    done < "${LOCAL_ENV_FILE}"
  fi
}

usage() {
  cat <<USAGE
Usage: hvo-env.sh <export|load|auto>
  export  Export variables from devcontainer.local.env to current shell (source this script)
  load    Load variables with a short report (source this script)
  auto    Auto-load if key vars are missing (for shell startup)
USAGE
}

main() {
  local cmd="${1:-}"
  case "${cmd}" in
    export) cmd_export ;;
    load)   cmd_load ;;
    auto)   cmd_auto ;;
    *) usage; exit 1 ;;
  esac
}

(
  # Execute main logic in a subshell-like scope that enables strict mode,
  # but still exports variables into the parent shell since we use export.
  # We can't use a real subshell if we want exports to persist, so we just
  # group the strict mode with the call, and restoration happens after.
  set -euo pipefail
  main "$@"
)

# Restore caller shell options if we were sourced
if (( __HVO_ENV_SOURCED )) && [[ -n "${__HVO_ENV_ORIG_SET_OPTS}" ]]; then
  eval "${__HVO_ENV_ORIG_SET_OPTS}"
fi
