#!/usr/bin/env bash
set -euo pipefail

echo "[kill-skymonitorv5] Checking for running SkyMonitorV5 processes..."
PIDS1=$(ps -eo pid,cmd | awk '/dotnet .*HVO.SkyMonitorV5.RPi\.dll/ && !/awk/ {print $1}') || true
PIDS2=""
if command -v lsof >/dev/null 2>&1; then
  PIDS2="$(lsof -t -i :7151 -sTCP:LISTEN 2>/dev/null; lsof -t -i :5136 -sTCP:LISTEN 2>/dev/null)" || true
fi
PIDS=$(printf '%s\n%s' "$PIDS1" "$PIDS2" | sed '/^$/d' | sort -u)

if [ -n "$PIDS" ]; then
  echo "[kill-skymonitorv5] Killing PIDs: $PIDS"
  kill -9 $PIDS || true
else
  echo "[kill-skymonitorv5] No running SkyMonitorV5 processes found."
fi

echo "[kill-skymonitorv5] Done."
