#!/usr/bin/env bash
# Advanced secret management for dev containers
# This script can integrate with various secret management services

set -euo pipefail

log() {
  echo "[secret-manager] $*"
}

# Azure Key Vault Integration
fetch_from_azure_keyvault() {
  local vault_name="$1"
  local secret_name="$2"
  local env_var_name="$3"
  
  if command -v az >/dev/null 2>&1; then
    log "Fetching ${secret_name} from Azure Key Vault ${vault_name}..."
    
    if az account show >/dev/null 2>&1; then
      local secret_value
      if secret_value=$(az keyvault secret show --vault-name "${vault_name}" --name "${secret_name}" --query value -o tsv 2>/dev/null); then
        export "${env_var_name}=${secret_value}"
        log "Successfully set ${env_var_name} from Azure Key Vault"
        return 0
      else
        log "Failed to fetch ${secret_name} from Azure Key Vault"
      fi
    else
      log "Not authenticated with Azure CLI"
    fi
  else
    log "Azure CLI not available"
  fi
  return 1
}

# GitHub CLI Integration (for accessing GitHub secrets in local development)
fetch_from_github_secrets() {
  local repo="$1"
  local secret_name="$2"
  local env_var_name="$3"
  
  if command -v gh >/dev/null 2>&1; then
    log "Attempting to fetch ${secret_name} from GitHub..."
    
    if gh auth status >/dev/null 2>&1; then
      # Note: GitHub doesn't provide an API to read secret values for security
      # But we can check if secrets exist and guide the user
      log "GitHub CLI is authenticated, but secret values cannot be fetched directly"
      log "GitHub secrets are only available in GitHub Actions and Codespaces"
      return 1
    else
      log "Not authenticated with GitHub CLI"
    fi
  else
    log "GitHub CLI not available"
  fi
  return 1
}

# AWS Secrets Manager Integration
fetch_from_aws_secrets() {
  local secret_name="$1"
  local env_var_name="$2"
  
  if command -v aws >/dev/null 2>&1; then
    log "Fetching ${secret_name} from AWS Secrets Manager..."
    
    local secret_value
    if secret_value=$(aws secretsmanager get-secret-value --secret-id "${secret_name}" --query SecretString --output text 2>/dev/null); then
      export "${env_var_name}=${secret_value}"
      log "Successfully set ${env_var_name} from AWS Secrets Manager"
      return 0
    else
      log "Failed to fetch ${secret_name} from AWS Secrets Manager"
    fi
  else
    log "AWS CLI not available"
  fi
  return 1
}

# HashiCorp Vault Integration
fetch_from_vault() {
  local vault_addr="$1"
  local secret_path="$2"
  local secret_key="$3"
  local env_var_name="$4"
  
  if command -v vault >/dev/null 2>&1; then
    export VAULT_ADDR="${vault_addr}"
    log "Fetching ${secret_path}/${secret_key} from HashiCorp Vault..."
    
    local secret_value
    if secret_value=$(vault kv get -field="${secret_key}" "${secret_path}" 2>/dev/null); then
      export "${env_var_name}=${secret_value}"
      log "Successfully set ${env_var_name} from HashiCorp Vault"
      return 0
    else
      log "Failed to fetch ${secret_path}/${secret_key} from HashiCorp Vault"
    fi
  else
    log "Vault CLI not available"
  fi
  return 1
}

# macOS Keychain Integration (for local development)
fetch_from_macos_keychain() {
  local service="$1"
  local account="$2"
  local env_var_name="$3"
  
  if [[ "$(uname)" == "Darwin" ]] && command -v security >/dev/null 2>&1; then
    log "Fetching ${service}/${account} from macOS Keychain..."
    
    local secret_value
    if secret_value=$(security find-generic-password -s "${service}" -a "${account}" -w 2>/dev/null); then
      export "${env_var_name}=${secret_value}"
      log "Successfully set ${env_var_name} from macOS Keychain"
      return 0
    else
      log "Failed to fetch ${service}/${account} from macOS Keychain"
    fi
  else
    log "macOS Keychain not available (not on macOS or security command not found)"
  fi
  return 1
}

# Detect environment and fetch secrets accordingly
detect_and_fetch_secrets() {
  log "Detecting environment and available secret sources..."
  
  # Check if we're in GitHub Codespaces
  if [[ -n "${CODESPACES:-}" ]]; then
    log "Running in GitHub Codespaces - secrets should be available as environment variables"
    return 0
  fi
  
  # Try different secret sources in order of preference
  local ssh_private_key_fetched=false
  local ssh_public_key_fetched=false
  
  # Try Azure Key Vault (if configured)
  if [[ -n "${HVO_AZURE_KEYVAULT_NAME:-}" ]]; then
    fetch_from_azure_keyvault "${HVO_AZURE_KEYVAULT_NAME}" "hvo-ssh-private-key" "HVO_SECRET__SSH__PRIVATE_KEY_B64" && ssh_private_key_fetched=true
    fetch_from_azure_keyvault "${HVO_AZURE_KEYVAULT_NAME}" "hvo-ssh-public-key" "HVO_SECRET__SSH__PUBLIC_KEY_B64" && ssh_public_key_fetched=true
  fi
  
  # Try AWS Secrets Manager (if configured)
  if [[ -n "${HVO_AWS_SECRET_NAME:-}" ]] && [[ "${ssh_private_key_fetched}" == "false" ]]; then
    fetch_from_aws_secrets "${HVO_AWS_SECRET_NAME}/ssh-private-key" "HVO_SECRET__SSH__PRIVATE_KEY_B64" && ssh_private_key_fetched=true
    fetch_from_aws_secrets "${HVO_AWS_SECRET_NAME}/ssh-public-key" "HVO_SECRET__SSH__PUBLIC_KEY_B64" && ssh_public_key_fetched=true
  fi
  
  # Try macOS Keychain (for local development)
  if [[ "${ssh_private_key_fetched}" == "false" ]]; then
    fetch_from_macos_keychain "HVO" "ssh-private-key-b64" "HVO_SECRET__SSH__PRIVATE_KEY_B64" && ssh_private_key_fetched=true
    fetch_from_macos_keychain "HVO" "ssh-public-key-b64" "HVO_SECRET__SSH__PUBLIC_KEY_B64" && ssh_public_key_fetched=true
  fi
  
  # Report results
  if [[ "${ssh_private_key_fetched}" == "true" && "${ssh_public_key_fetched}" == "true" ]]; then
    log "Successfully fetched SSH keys from external secret store"
  else
    log "Could not fetch SSH keys from external stores - falling back to local environment file"
  fi
}

main() {
  log "Advanced secret management for HVO dev containers"
  detect_and_fetch_secrets
}

# Only run if called directly (not sourced)
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  main "$@"
fi