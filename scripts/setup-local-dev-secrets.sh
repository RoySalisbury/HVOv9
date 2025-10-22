#!/usr/bin/env bash
# Setup script for local dev container secrets
# This script helps set up the devcontainer.local.env file for local development

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCAL_ENV_FILE="${REPO_ROOT}/.devcontainer/devcontainer.local.env"
EXAMPLE_FILE="${REPO_ROOT}/.devcontainer/devcontainer.local.env.example"

log() {
  echo "[local-secrets] $*"
}

create_local_env_file() {
  if [[ -f "${LOCAL_ENV_FILE}" ]]; then
    log "Local environment file already exists: ${LOCAL_ENV_FILE}"
    log "To recreate it, delete the file first and run this script again."
    return 0
  fi

  log "Creating local environment file from example..."
  cp "${EXAMPLE_FILE}" "${LOCAL_ENV_FILE}"
  log "Created: ${LOCAL_ENV_FILE}"
}

prompt_for_ssh_keys() {
  local private_key_file
  local public_key_file
  
  log ""
  log "SSH Key Setup for Docker Remote Contexts:"
  log "This will encode your SSH keys as base64 for use with remote Docker hosts."
  log ""
  
  read -p "Enter path to your SSH private key (or press Enter to skip): " private_key_file
  
  if [[ -n "${private_key_file}" && -f "${private_key_file}" ]]; then
    log "Encoding private key..."
    local private_key_b64
    private_key_b64="$(base64 -i "${private_key_file}" | tr -d '\n')"
    
    # Update the local env file
    if grep -q "^HVO_SECRET__SSH__PRIVATE_KEY_B64=" "${LOCAL_ENV_FILE}"; then
      sed -i.bak "s|^HVO_SECRET__SSH__PRIVATE_KEY_B64=.*|HVO_SECRET__SSH__PRIVATE_KEY_B64=${private_key_b64}|" "${LOCAL_ENV_FILE}"
    else
      echo "HVO_SECRET__SSH__PRIVATE_KEY_B64=${private_key_b64}" >> "${LOCAL_ENV_FILE}"
    fi
    
    log "Private key encoded and added to local environment file."
    
    # Check for corresponding public key
    local public_key_candidate="${private_key_file}.pub"
    if [[ -f "${public_key_candidate}" ]]; then
      log "Found corresponding public key: ${public_key_candidate}"
      local public_key_b64
      public_key_b64="$(base64 -i "${public_key_candidate}" | tr -d '\n')"
      
      if grep -q "^HVO_SECRET__SSH__PUBLIC_KEY_B64=" "${LOCAL_ENV_FILE}"; then
        sed -i.bak "s|^HVO_SECRET__SSH__PUBLIC_KEY_B64=.*|HVO_SECRET__SSH__PUBLIC_KEY_B64=${public_key_b64}|" "${LOCAL_ENV_FILE}"
      else
        echo "HVO_SECRET__SSH__PUBLIC_KEY_B64=${public_key_b64}" >> "${LOCAL_ENV_FILE}"
      fi
      
      log "Public key encoded and added to local environment file."
    else
      log "No corresponding public key found at ${public_key_candidate}"
      read -p "Enter path to your SSH public key (or press Enter to skip): " public_key_file
      
      if [[ -n "${public_key_file}" && -f "${public_key_file}" ]]; then
        local public_key_b64
        public_key_b64="$(base64 -i "${public_key_file}" | tr -d '\n')"
        
        if grep -q "^HVO_SECRET__SSH__PUBLIC_KEY_B64=" "${LOCAL_ENV_FILE}"; then
          sed -i.bak "s|^HVO_SECRET__SSH__PUBLIC_KEY_B64=.*|HVO_SECRET__SSH__PUBLIC_KEY_B64=${public_key_b64}|" "${LOCAL_ENV_FILE}"
        else
          echo "HVO_SECRET__SSH__PUBLIC_KEY_B64=${public_key_b64}" >> "${LOCAL_ENV_FILE}"
        fi
        
        log "Public key encoded and added to local environment file."
      fi
    fi
    
    # Clean up backup file
    [[ -f "${LOCAL_ENV_FILE}.bak" ]] && rm "${LOCAL_ENV_FILE}.bak"
    
  else
    log "SSH key setup skipped."
  fi
}

try_github_cli_secrets() {
  if ! command -v gh >/dev/null 2>&1; then
    log "GitHub CLI not available. Cannot fetch repository secrets."
    return 1
  fi

  if ! gh auth status >/dev/null 2>&1; then
    log "Not authenticated with GitHub CLI. Cannot fetch repository secrets."
    return 1
  fi

  log ""
  log "Attempting to fetch secrets from GitHub repository..."
  log "Note: This will only work for secrets you have access to."
  
  # Try to fetch some non-sensitive info to test access
  local repo_info
  if repo_info="$(gh repo view --json name,owner 2>/dev/null)"; then
    log "GitHub CLI access confirmed for repository."
    log "Unfortunately, GitHub doesn't provide an API to read repository secret values for security reasons."
    log "You'll need to set up secrets manually in the local environment file."
    return 1
  else
    log "Cannot access repository information via GitHub CLI."
    return 1
  fi
}

show_setup_instructions() {
  log ""
  log "=== Local Dev Container Secrets Setup Complete ==="
  log ""
  log "Next steps:"
  log "1. Edit the file: ${LOCAL_ENV_FILE}"
  log "2. Replace the example values with your actual secrets"
  log "3. Rebuild your dev container to pick up the new environment variables"
  log ""
  log "Key secrets to configure:"
  log "  - Database connection strings (if using real databases)"
  log "  - API keys (NINA, Azure DevOps)"
  log "  - SSH keys (already configured if you provided them)"
  log "  - Tailscale auth key (for VPN access)"
  log ""
  log "The file ${LOCAL_ENV_FILE} is git-ignored and won't be committed."
  log ""
  log "For GitHub Codespaces, use repository secrets instead:"
  log "  bash scripts/setup-codespaces-secrets.sh"
}

main() {
  log "Setting up local dev container secrets..."
  log "Repo root: ${REPO_ROOT}"
  
  # Create the local environment file from example
  create_local_env_file
  
  # Try to help with SSH keys
  prompt_for_ssh_keys
  
  # Try GitHub CLI (will likely fail but worth trying)
  try_github_cli_secrets || true
  
  # Show final instructions
  show_setup_instructions
}

main "$@"