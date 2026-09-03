#!/usr/bin/env bash
# tools/foreman_watchdog.sh — Kill stuck foreman processes
#
# Cron every 5 minutes. If /tmp/runewake_foreman.pid exists and that process
# has been alive > 75 minutes, kill its process tree, remove the lock, and
# post ONE line to the Runewake Telegram group.
#
# Never touch a process younger than 75 minutes.

set -euo pipefail

LOCK_PID_FILE="/tmp/runewake_foreman.pid"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
MAX_AGE_SECONDS=$((75 * 60))  # 75 minutes

if [[ ! -f "${LOCK_PID_FILE}" ]]; then
  exit 0
fi

PID=$(cat "${LOCK_PID_FILE}" 2>/dev/null || echo "")

if [[ -z "${PID}" ]]; then
  rm -f "${LOCK_PID_FILE}"
  exit 0
fi

# Check if process exists
if ! kill -0 "${PID}" 2>/dev/null; then
  # Process gone — clean up stale lock
  rm -f "${LOCK_PID_FILE}"
  exit 0
fi

# Get elapsed time in seconds
ELAPSED=$(ps -o etimes= -p "${PID}" 2>/dev/null || echo "0")
ELAPSED=${ELAPSED// /}

if [[ -z "${ELAPSED}" ]] || ! [[ "${ELAPSED}" =~ ^[0-9]+$ ]]; then
  exit 0
fi

if [[ "${ELAPSED}" -lt "${MAX_AGE_SECONDS}" ]]; then
  # Process is still young — leave it alone
  exit 0
fi

# Find what it was doing
WHAT=$(ps -o cmd= -p "${PID}" 2>/dev/null | head -c 200 || echo "unknown")

# Kill the entire process tree (hermes, godot, xvfb, dotnet children)
# Get all descendant PIDs using pstree
PID_LIST=$(pstree -p "${PID}" 2>/dev/null | grep -oP '\d+' | sort -rn | tr '\n' ' ')
if [[ -n "${PID_LIST}" ]]; then
  kill -9 ${PID_LIST} 2>/dev/null || true
  sleep 1
  kill -9 ${PID_LIST} 2>/dev/null || true
fi

# Also kill any orphaned godot/xvfb-run processes that may have been left
# (these are common orphans from a stuck foreman)
for orphan_pid in $(ps -o pid=,etimes= -C godot 2>/dev/null | awk -v max="${MAX_AGE_SECONDS}" '$2 > max {print $1}' || true); do
  kill -9 "${orphan_pid}" 2>/dev/null || true
done
for orphan_pid in $(ps -o pid=,etimes= -C xvfb-run 2>/dev/null | awk -v max="${MAX_AGE_SECONDS}" '$2 > max {print $1}' || true); do
  kill -9 "${orphan_pid}" 2>/dev/null || true
done

rm -f "${LOCK_PID_FILE}"

# Post ONE line to the group
"${HOME}/.local/bin/hermes" send --to "telegram:Runewake" "watchdog: killed a stuck foreman (${WHAT})" 2>/dev/null || true