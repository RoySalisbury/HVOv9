#!/usr/bin/env bash
# Setup script for Codespaces environment variables
# This script helps configure repository secrets for GitHub Codespaces

set -euo pipefail

REPO="RoySalisbury/HVOv9"

log() {
  echo "[codespaces-setup] $*"
}

check_gh_cli() {
  if ! command -v gh >/dev/null 2>&1; then
    log "ERROR: GitHub CLI (gh) is required but not found."
    log "Install it from: https://cli.github.com/"
    exit 1
  fi

  if ! gh auth status >/dev/null 2>&1; then
    log "ERROR: Not authenticated with GitHub CLI."
    log "Run: gh auth login"
    exit 1
  fi
}

set_codespace_secret() {
  local name="$1"
  local description="$2"
  local default_value="${3:-}"
  
  log "Setting up Codespace secret: ${name}"
  log "Description: ${description}"
  
  if [[ -n "${default_value}" ]]; then
    log "Default value provided. Use 'gh codespace secret set ${name} --repos ${REPO}' to set it."
  else
    log "No default - configure manually: gh codespace secret set ${name} --repos ${REPO}"
  fi
}

main() {
  log "Setting up GitHub Codespaces secrets for ${REPO}"
  
  check_gh_cli
  
  log ""
  log "The following Codespace secrets should be configured for full functionality:"
  log "Use 'gh codespace secret set SECRET_NAME --repos ${REPO}' to set each one."
  log ""
  
  set_codespace_secret "HVO_SECRET__WEBSITEV9__DB_CONNECTION" \
    "Database connection string for HVO.WebSite.v9"
  
  set_codespace_secret "HVO_SECRET__WEBSITEPLAYGROUND__DB_CONNECTION" \
    "Database connection string for HVO.WebSite.Playground"
  
  set_codespace_secret "HVO_SECRET__WEBSITEPLAYGROUND__NINA_API_KEY" \
    "NINA API key for playground site (optional)"
  
  set_codespace_secret "HVO_SECRET__WEBSITEPLAYGROUND__AZDO_PAT" \
    "Azure DevOps Personal Access Token (optional)"
  
  set_codespace_secret "HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_ACCESSKEY" \
    "S3 access key for SkyMonitor V5 frame export" \
    "admin"
  
  set_codespace_secret "HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_SECRETKEY" \
    "S3 secret key for SkyMonitor V5 frame export" \
    "change-me-now-32chars"
  
  set_codespace_secret "HVO_SECRET__SSH__PRIVATE_KEY_B64" \
    "Base64-encoded SSH private key (optional)"
  
  set_codespace_secret "HVO_SECRET__SSH__PUBLIC_KEY_B64" \
    "Base64-encoded SSH public key (optional)"
  
  set_codespace_secret "TS_AUTHKEY" \
    "Tailscale auth key (optional)"
  
  log ""
  log "Example commands:"
  log "  gh codespace secret set HVO_SECRET__WEBSITEV9__DB_CONNECTION --repos ${REPO}"
  log "  gh codespace secret set TS_AUTHKEY --repos ${REPO}"
  log ""
  log "For development defaults, the devcontainer will use safe fallbacks."
  log "Critical secrets (like database connections) should be set for production scenarios."
}

main "$@"