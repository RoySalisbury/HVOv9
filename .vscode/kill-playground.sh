#!/usr/bin/env bash
set -euo pipefail

echo "[kill-playground] Checking for running Playground processes..."
PIDS1=$(ps -eo pid,cmd | awk '/dotnet .*HVO.WebSite.Playground\.dll/ && !/awk/ {print $1}') || true
PIDS2=""
if command -v lsof >/dev/null 2>&1; then
  PIDS2="$(lsof -t -i :7151 -sTCP:LISTEN 2>/dev/null; lsof -t -i :5136 -sTCP:LISTEN 2>/dev/null)" || true
fi
PIDS=$(printf '%s
%s' "$PIDS1" "$PIDS2" | sed '/^$/d' | sort -u)

if [ -n "$PIDS" ]; then
  echo "[kill-playground] Killing PIDs: $PIDS"
  kill -9 $PIDS || true
else
  echo "[kill-playground] No running Playground processes found."
fi

echo "[kill-playground] Done."
