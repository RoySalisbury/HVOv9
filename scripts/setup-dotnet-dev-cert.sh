#!/usr/bin/env bash
set -euo pipefail

# Setup HTTPS development certificate for container development
# This is required because the Microsoft dev container doesn't include trusted certs
# See: https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl#trust-the-aspnet-core-https-development-certificate

log() {
  echo "[dev-cert] $*"
}

log "Setting up .NET HTTPS development certificate for container."

# Ensure .dotnet directory has correct ownership
if [[ -d "/home/vscode/.dotnet" ]]; then
  sudo chown -R vscode:vscode /home/vscode/.dotnet
fi

# Check if certificate already exists and is trusted
if dotnet dev-certs https --check --trust >/dev/null 2>&1; then
  log "HTTPS development certificate is already present and trusted."
  exit 0
fi

log "Generating new HTTPS development certificate."

# Generate development certificate (will create one if it doesn't exist)
dotnet dev-certs https --clean
dotnet dev-certs https

# For Linux containers, we need to manually trust the certificate
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
  log "Exporting and trusting certificate for Linux container."
  
  # Export to system certificate store
  sudo -E dotnet dev-certs https --export-path /usr/local/share/ca-certificates/dotnet-dev-cert.crt --format pem --no-password
  
  # Update CA certificates
  sudo update-ca-certificates
  
  log "Certificate trusted in container certificate store."
else
  # For non-Linux (shouldn't happen in container, but safe fallback)
  dotnet dev-certs https --trust
fi

# Verify the setup
if dotnet dev-certs https --check >/dev/null 2>&1; then
  log "HTTPS development certificate setup completed successfully."
else
  log "Warning: Certificate setup may not be complete. HTTPS development may not work properly."
fi
