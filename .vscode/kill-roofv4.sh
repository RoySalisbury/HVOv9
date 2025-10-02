#!/usr/bin/env bash
set -euo pipefail

echo "[kill-roofv4] Checking for running RoofControllerV4 processes..."
PIDS1=$(ps -eo pid,cmd | awk '/dotnet .*HVO.RoofControllerV4.RPi\.dll/ && !/awk/ {print $1}') || true
PIDS2=""
if command -v lsof >/dev/null 2>&1; then
  PIDS2="$(lsof -t -i :7151 -sTCP:LISTEN 2>/dev/null; lsof -t -i :5136 -sTCP:LISTEN 2>/dev/null)" || true
fi
PIDS=$(printf '%s\n%s' "$PIDS1" "$PIDS2" | sed '/^$/d' | sort -u)

if [ -n "$PIDS" ]; then
  echo "[kill-roofv4] Killing PIDs: $PIDS"
  kill -9 $PIDS || true
else
  echo "[kill-roofv4] No running RoofControllerV4 processes found."
fi

echo "[kill-roofv4] Done."
