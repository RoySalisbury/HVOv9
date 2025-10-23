#!/usr/bin/env bash
# Deprecated wrapper. Use: source scripts/hvo-env.sh auto

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash -c "source '${SCRIPT_DIR}/hvo-env.sh' auto"