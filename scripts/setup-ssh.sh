#!/usr/bin/env bash
set -euo pipefail

# Expected environment variables (configure via GitHub secrets or export locally):
#   HVO_SECRET__SSH__PRIVATE_KEY or HVO_SECRET__SSH__PRIVATE_KEY_B64
#   HVO_SECRET__SSH__PUBLIC_KEY or HVO_SECRET__SSH__PUBLIC_KEY_B64

PRIVATE_KEY_B64="${HVO_SECRET__SSH__PRIVATE_KEY_B64-}"
PRIVATE_KEY_RAW="${HVO_SECRET__SSH__PRIVATE_KEY-}"
PUBLIC_KEY_B64="${HVO_SECRET__SSH__PUBLIC_KEY_B64-}"
PUBLIC_KEY_RAW="${HVO_SECRET__SSH__PUBLIC_KEY-}"
SSH_DIR="${HOME}/.ssh"
KEY_NAME="id_hvo_docker"

write_file_from_var() {
  local value="$1"
  local path="$2"

  printf '%s' "${value}" >"${path}"
}

write_file_from_b64() {
  local value="$1"
  local path="$2"

  printf '%s' "${value}" | base64 --decode --ignore-garbage >"${path}"
}

if [[ -z "${PRIVATE_KEY_B64}" && -z "${PRIVATE_KEY_RAW}" && -z "${PUBLIC_KEY_B64}" && -z "${PUBLIC_KEY_RAW}" ]]; then
  echo "[ssh-setup] No SSH secrets provided. Skipping SSH key provisioning."
  exit 0
fi

echo "[ssh-setup] Configuring SSH material for Docker contexts."
mkdir -p "${SSH_DIR}"
chmod 700 "${SSH_DIR}"

PRIVATE_KEY_PATH="${SSH_DIR}/${KEY_NAME}"
PUBLIC_KEY_PATH="${SSH_DIR}/${KEY_NAME}.pub"

if [[ -n "${PRIVATE_KEY_B64}" ]]; then
  write_file_from_b64 "${PRIVATE_KEY_B64}" "${PRIVATE_KEY_PATH}"
elif [[ -n "${PRIVATE_KEY_RAW}" ]]; then
  write_file_from_var "${PRIVATE_KEY_RAW}" "${PRIVATE_KEY_PATH}"
fi

if [[ -n "${PUBLIC_KEY_B64}" ]]; then
  write_file_from_b64 "${PUBLIC_KEY_B64}" "${PUBLIC_KEY_PATH}"
elif [[ -n "${PUBLIC_KEY_RAW}" ]]; then
  write_file_from_var "${PUBLIC_KEY_RAW}" "${PUBLIC_KEY_PATH}"
fi

if [[ -f "${PRIVATE_KEY_PATH}" ]]; then
  chmod 600 "${PRIVATE_KEY_PATH}"
  echo "[ssh-setup] Wrote private key to ${PRIVATE_KEY_PATH}."
fi

if [[ -f "${PUBLIC_KEY_PATH}" ]]; then
  chmod 644 "${PUBLIC_KEY_PATH}"
  echo "[ssh-setup] Wrote public key to ${PUBLIC_KEY_PATH}."
fi

CONFIG_PATH="${SSH_DIR}/config"
if [[ -f "${PRIVATE_KEY_PATH}" ]]; then
  touch "${CONFIG_PATH}"
  chmod 600 "${CONFIG_PATH}"

  ensure_host_entry() {
    local host_alias="$1"
    local user_override="${2-}"
    if ! grep -q "^Host ${host_alias}" "${CONFIG_PATH}"; then
      {
        echo "Host ${host_alias}"
        if [[ -n "${user_override}" ]]; then
          echo "  User ${user_override}"
        fi
        echo "  IdentityFile ${PRIVATE_KEY_PATH}"
        echo "  IdentitiesOnly yes"
      } >>"${CONFIG_PATH}"
      echo "[ssh-setup] Added SSH config for '${host_alias}'."
    fi
  }

  ensure_host_entry "hvo-docker"
  ensure_host_entry "192.168.2.3" "roys"
  ensure_host_entry "192.168.2.104" "roys"
fi

if [[ -f "${PRIVATE_KEY_PATH}" && -n "${SSH_AUTH_SOCK-}" ]]; then
  ssh-add "${PRIVATE_KEY_PATH}" >/dev/null 2>&1 || true
  echo "[ssh-setup] Added key to existing ssh-agent."
fi

echo "[ssh-setup] SSH bootstrap complete (missing secrets were skipped)."
