#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APT_PACKAGES=(
  gh
  libgpiod-dev
  i2c-tools
  fontconfig
  fonts-dejavu-core
  fonts-liberation2
  fonts-noto-core
  fonts-noto-mono
  fonts-noto-color-emoji
  fonts-roboto
  fonts-open-sans
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
  python3-venv
  python3-numpy
  python3-pil
  lsb-release
  net-tools
  iproute2
  ripgrep
  iputils-ping
  sqlite3
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
  log "Regenerating font cache."
  sudo fc-cache -f
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

  if dotnet tool list -g | grep -q '^dotnet-dump\s'; then
    log "Updating dotnet-dump global tool."
    dotnet tool update --global dotnet-dump
  else
    log "Installing dotnet-dump global tool."
    dotnet tool install --global dotnet-dump
  fi

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
  log "Copying catalog data."
  bash "${REPO_ROOT}/scripts/copy-catalog.sh"

  add_github_cli_repo
  install_packages
  configure_git_identity

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
