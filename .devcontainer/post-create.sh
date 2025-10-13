#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APT_PACKAGES=(
  gh
  libgpiod-dev
  i2c-tools
  git
  curl
  wget
  unzip
  zip
  tar
  gzip
  make
  build-essential
  pkg-config
  libssl-dev
  ca-certificates
  jq
  nano
  python3
  python3-pip
  lsb-release
  net-tools
  iproute2
)

log() {
  echo "[post-create] $*"
}

add_github_cli_repo() {
  if dpkg -s gh >/dev/null 2>&1; then
    return
  fi

  if [[ -f /etc/apt/sources.list.d/github-cli.list ]]; then
    return
  fi

  log "Adding GitHub CLI apt repository."
  curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
    | sudo dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg status=none
  sudo chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg

  echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
    | sudo tee /etc/apt/sources.list.d/github-cli.list >/dev/null
}

install_packages() {
  log "Updating apt package index."
  sudo apt-get update

  log "Installing apt packages: ${APT_PACKAGES[*]}."
  sudo apt-get install -y "${APT_PACKAGES[@]}"
}

install_dotnet_tools() {
  log "Installing wasm-tools workload."
  dotnet workload install wasm-tools

  if dotnet tool list -g | grep -q '^dotnet-ef\s'; then
    log "Updating dotnet-ef global tool."
    dotnet tool update --global dotnet-ef
  else
    log "Installing dotnet-ef global tool."
    dotnet tool install --global dotnet-ef
  fi
}

main() {
  log "Copying catalog data."
  bash "${REPO_ROOT}/.devcontainer/copy-catalogs.sh"

  add_github_cli_repo
  install_packages

  log "Adding vscode user to i2c group."
  sudo usermod -aG i2c vscode

  install_dotnet_tools

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

  log "Post-create provisioning complete."
}

main "$@"
