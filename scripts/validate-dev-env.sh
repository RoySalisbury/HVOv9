#!/usr/bin/env bash
# Validate dev container environment variables
# This script checks if all expected environment variables are properly set

set -euo pipefail

log() {
  echo "[env-validator] $*"
}

validate_env_var() {
  local var_name="$1"
  local expected_type="$2"  # "required", "optional", "base64"
  local description="$3"
  
  local value="${!var_name:-}"
  
  if [[ -z "${value}" ]]; then
    if [[ "${expected_type}" == "required" ]]; then
      log "❌ MISSING: ${var_name} (${description})"
      return 1
    else
      log "⚠️  EMPTY: ${var_name} (${description}) - using default"
      return 0
    fi
  fi
  
  # Validate base64 encoded values
  if [[ "${expected_type}" == "base64" ]]; then
    # Remove common URL encoding artifacts (% at end)
    local clean_value="${value%\%}"
    if echo "${clean_value}" | base64 -d >/dev/null 2>&1; then
      log "✅ VALID: ${var_name} (${description}) - base64 encoded, ${#value} chars"
    else
      log "❌ INVALID: ${var_name} (${description}) - not valid base64"
      return 1
    fi
  else
    # Show length for security (don't show actual values)
    local display_length="${#value}"
    if [[ "${display_length}" -gt 50 ]]; then
      log "✅ VALID: ${var_name} (${description}) - ${display_length} chars"
    else
      log "✅ VALID: ${var_name} (${description}) - '${value}'"
    fi
  fi
  
  return 0
}

main() {
  log "Validating HVO dev container environment variables..."
  log ""
  
  local errors=0
  
  # Database connections
  validate_env_var "HVO_SECRET__WEBSITEV9__DB_CONNECTION" "optional" "WebSite v9 database connection" || ((errors++))
  validate_env_var "HVO_SECRET__WEBSITEPLAYGROUND__DB_CONNECTION" "optional" "Playground database connection" || ((errors++))
  
  # API keys
  validate_env_var "HVO_SECRET__WEBSITEPLAYGROUND__NINA_API_KEY" "optional" "NINA API key for playground" || ((errors++))
  validate_env_var "HVO_SECRET__WEBSITEPLAYGROUND__AZDO_PAT" "optional" "Azure DevOps PAT for playground" || ((errors++))
  
  # S3 credentials
  validate_env_var "HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_ACCESSKEY" "optional" "S3 access key for frame export" || ((errors++))
  validate_env_var "HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_SECRETKEY" "optional" "S3 secret key for frame export" || ((errors++))
  
  # SSH keys (these are the most important for Docker contexts)
  validate_env_var "HVO_SECRET__SSH__PRIVATE_KEY_B64" "base64" "SSH private key for Docker contexts" || ((errors++))
  validate_env_var "HVO_SECRET__SSH__PUBLIC_KEY_B64" "base64" "SSH public key for Docker contexts" || ((errors++))
  
  # Tailscale
  validate_env_var "TS_AUTHKEY" "optional" "Tailscale auth key" || ((errors++))
  validate_env_var "TS_HOSTNAME" "optional" "Tailscale hostname" || ((errors++))
  
  log ""
  if [[ "${errors}" -eq 0 ]]; then
    log "✅ All environment variables are properly configured!"
    log ""
    log "SSH Keys Status:"
    if [[ -n "${HVO_SECRET__SSH__PRIVATE_KEY_B64:-}" ]] && [[ -n "${HVO_SECRET__SSH__PUBLIC_KEY_B64:-}" ]]; then
      log "  - SSH keys are available for Docker remote contexts"
      log "  - Private key: ${#HVO_SECRET__SSH__PRIVATE_KEY_B64} characters"
      log "  - Public key: ${#HVO_SECRET__SSH__PUBLIC_KEY_B64} characters"
    else
      log "  - SSH keys not configured (Docker remote contexts will not work)"
    fi
    
    log ""
    log "Database Status:"
    if [[ -n "${HVO_SECRET__WEBSITEV9__DB_CONNECTION:-}" ]]; then
      log "  - WebSite v9 database configured"
    else
      log "  - WebSite v9 using default localhost database"
    fi
    
    return 0
  else
    log "❌ Found ${errors} issues with environment variable configuration"
    log ""
    log "To fix issues:"
    log "1. Check your devcontainer.local.env file"
    log "2. Verify base64 encoding for SSH keys"
    log "3. Rebuild the dev container"
    return 1
  fi
}

main "$@"