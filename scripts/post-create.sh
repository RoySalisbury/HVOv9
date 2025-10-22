#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APT_PACKAGES=(
  # IoT/Hardware development (HVO-specific)
  libgpiod-dev
  i2c-tools
  
  # Fonts for image processing and UI (HVO sky monitoring)
  fontconfig
  fonts-dejavu-core
  fonts-liberation2
  fonts-noto-core
  fonts-noto-mono
  fonts-noto-color-emoji
  fonts-roboto
  fonts-open-sans
  
  # Python packages for analysis scripts (now handled by Python feature, but keep for completeness)
  python3-numpy
  python3-pil
  
  # Additional utilities not guaranteed in base image
  wget
  ripgrep
  sqlite3
  iputils-ping
)

log() {
  echo "[post-create] $*"
}

# GitHub CLI setup removed - now handled by dev container feature

install_packages() {
  log "Updating apt package index."
  sudo apt-get update

  log "Installing apt packages: ${APT_PACKAGES[*]}."
  if sudo apt-get install -y "${APT_PACKAGES[@]}"; then
    log "Successfully installed packages."
  else
    log "Warning: Some packages may have failed to install. Continuing..."
  fi
  
  log "Regenerating font cache."
  sudo fc-cache -f
}

fix_dotnet_permissions() {
  # Fix .NET directory ownership for vscode user
  # The mcr.microsoft.com/devcontainers/dotnet:9.0 base image may create the .dotnet
  # directory with root ownership during initial setup, which prevents the vscode user
  # from using dotnet workloads and tools. This function ensures proper ownership.
  
  # Create the directory with correct ownership if it doesn't exist
  if [[ ! -d "/home/vscode/.dotnet" ]]; then
    log "Creating .NET directory with correct permissions."
    sudo mkdir -p /home/vscode/.dotnet
    sudo chown vscode:vscode /home/vscode/.dotnet
  fi
  
  # Always fix permissions on existing directory and contents
  log "Ensuring .NET directory permissions are correct for vscode user."
  sudo chown -R vscode:vscode /home/vscode/.dotnet/
  
  # Set proper permissions on the directory
  sudo chmod 755 /home/vscode/.dotnet/
  
  log ".NET directory permissions fixed."
}

setup_shell_environment() {
  # Set up automatic environment variable loading for all new shells
  # This ensures that environment variables from devcontainer.local.env
  # are available in every terminal session without manual intervention
  
  local bashrc_file="/home/vscode/.bashrc"
  local init_script="${REPO_ROOT}/scripts/init-shell-env.sh"
  local source_line="source '${init_script}'"
  
  # Check if the source line is already present
  if ! grep -Fq "${source_line}" "${bashrc_file}"; then
    log "Adding automatic environment loading to .bashrc"
    echo "" >> "${bashrc_file}"
    echo "# HVO Dev Container: Load environment variables automatically" >> "${bashrc_file}"
    echo "${source_line}" >> "${bashrc_file}"
    log "Shell environment setup complete. Environment variables will be available in new shells."
  else
    log "Shell environment already configured."
  fi
}

install_dotnet_tools() {
  # Ensure .NET directory has correct permissions before installing tools
  fix_dotnet_permissions
  
  # Install wasm-tools workload if not already present
  if ! dotnet workload list | grep -q 'wasm-tools'; then
    log "Installing wasm-tools workload."
    dotnet workload install wasm-tools
  else
    log "wasm-tools workload already installed."
  fi

  # Entity Framework Core CLI tools
  if dotnet tool list -g | grep -q '^dotnet-ef\s'; then
    log "Updating dotnet-ef global tool."
    dotnet tool update --global dotnet-ef
  else
    log "Installing dotnet-ef global tool."
    dotnet tool install --global dotnet-ef
  fi

  # Diagnostic tools for production troubleshooting
  if dotnet tool list -g | grep -q '^dotnet-dump\s'; then
    log "Updating dotnet-dump global tool."
    dotnet tool update --global dotnet-dump
  else
    log "Installing dotnet-dump global tool."
    dotnet tool install --global dotnet-dump
  fi

  # C# scripting support (useful for automation)
  if dotnet tool list -g | grep -q '^dotnet-script\s'; then
    log "Updating dotnet-script global tool."
    dotnet tool update --global dotnet-script
  else
    log "Installing dotnet-script global tool."
    dotnet tool install --global dotnet-script
  fi
}

configure_git_identity() {
  local existing_name existing_email identity_line gh_name gh_login gh_email primary_email

  existing_name="$(git config --global user.name 2>/dev/null || true)"
  existing_email="$(git config --global user.email 2>/dev/null || true)"

  if [[ -n "${existing_name}" && -n "${existing_email}" ]]; then
    log "Git identity already configured; skipping."
    return
  fi

  if ! command -v gh >/dev/null 2>&1; then
    log "GitHub CLI not available; skipping git identity configuration."
    return
  fi

  if ! gh auth status >/dev/null 2>&1; then
    log "GitHub CLI not authenticated; skipping git identity configuration."
    return
  fi

  identity_line="$(gh api user --jq '[.name // "", .login // "", .email // ""] | @tsv' 2>/dev/null || true)"
  if [[ -n "${identity_line}" ]]; then
    IFS=$'\t' read -r gh_name gh_login gh_email <<<"${identity_line}"
  fi

  if [[ -z "${gh_email}" ]]; then
    primary_email="$(gh api user/emails --jq '.[] | select(.primary == true) | .email' 2>/dev/null | head -n1 || true)"
    gh_email="${primary_email}";
  fi

  if [[ -z "${existing_name}" ]]; then
    if [[ -n "${gh_name}" ]]; then
      git config --global user.name "${gh_name}"
      log "Configured git user.name from GitHub profile."
    elif [[ -n "${gh_login}" ]]; then
      git config --global user.name "${gh_login}"
      log "Configured git user.name from GitHub login."
    else
      log "Unable to determine git user.name; leaving unset."
    fi
  fi

  if [[ -z "${existing_email}" ]]; then
    if [[ -n "${gh_email}" ]]; then
      git config --global user.email "${gh_email}"
      log "Configured git user.email from GitHub profile."
    elif [[ -n "${GIT_AUTHOR_EMAIL-}" ]]; then
      git config --global user.email "${GIT_AUTHOR_EMAIL}"
      log "Configured git user.email from environment."
    else
      log "Unable to determine git user.email; leaving unset."
    fi
  fi
}

main() {
  # Fix .NET permissions FIRST to prevent access issues during container setup
  fix_dotnet_permissions
  
  log "Copying catalog data."
  bash "${REPO_ROOT}/scripts/copy-catalog.sh"

  install_packages
  configure_git_identity

  log "Adding vscode user to i2c group for hardware access."
  if sudo usermod -aG i2c vscode; then
    log "Successfully added vscode user to i2c group."
  else
    log "Warning: Failed to add vscode user to i2c group. Hardware features may not work."
  fi

  # Try to fetch secrets from external sources (Azure Key Vault, AWS, etc.)
  log "Attempting to fetch secrets from external sources..."
  if bash "${REPO_ROOT}/scripts/advanced-secret-manager.sh"; then
    log "Advanced secret management completed."
  else
    log "Advanced secret management failed or not configured - loading local environment file manually."
    
    # Fallback: use dedicated environment loading script
    log "Loading environment variables using dedicated loader..."
    if source "${REPO_ROOT}/scripts/load-local-env.sh"; then
      log "Environment variables loaded successfully via dedicated loader."
    else
      log "Failed to load environment variables. Manual setup may be required."
    fi
  fi

  install_dotnet_tools

  log "Setting up HTTPS development certificate."
  bash "${REPO_ROOT}/scripts/setup-dotnet-dev-cert.sh"

  log "Provisioning user secrets."
  bash "${REPO_ROOT}/scripts/setup-user-secrets.sh"

  log "Provisioning SSH configuration."
  bash "${REPO_ROOT}/scripts/setup-ssh.sh"

  if command -v docker >/dev/null 2>&1; then
    log "Ensuring Docker contexts are present."
    if ! bash "${REPO_ROOT}/scripts/setup-docker-contexts.sh"; then
      log "Docker context setup failed; continuing without custom contexts."
    fi
  else
    log "Docker CLI not found; skipping Docker context setup."
  fi

  log "Setting up persistent shell environment loading."
  setup_shell_environment

  log "Post-create provisioning complete."
}

main "$@"
