#!/usr/bin/env bash
set -euo pipefail

# Determine workspace root as parent of this .vscode directory
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
WORKSPACE_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CERT_DIR="$WORKSPACE_ROOT/.certs"
CERT_PATH="$CERT_DIR/https-devcert.pfx"
CERT_PASSWORD="${ASPNETCORE_Kestrel__Certificates__Default__Password:-hvo_dev_password}"

mkdir -p "$CERT_DIR"

# Ensure a dev cert exists; export it to the workspace if not present
if [ ! -f "$CERT_PATH" ]; then
  echo "[setup-https] Exporting dev cert to $CERT_PATH"
  # Create if missing
  dotnet dev-certs https --check -q || dotnet dev-certs https -q
  # Export to a PFX used by Kestrel in the container
  dotnet dev-certs https -ep "$CERT_PATH" -p "$CERT_PASSWORD"
fi

echo "[setup-https] Dev cert ready: $CERT_PATH"
