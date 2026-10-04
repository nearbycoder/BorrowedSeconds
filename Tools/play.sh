#!/usr/bin/env bash
# Runs the built game. On this machine the X11/XWayland path can hang at startup, so use
# Unity's native Wayland backend when a Wayland session is available.
#   Tools/play.sh [dev] [extra player args...]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME="$ROOT/Builds/Linux/BorrowedSeconds.x86_64"
if [ "${1:-}" = "dev" ]; then GAME="$ROOT/Builds/LinuxDev/BorrowedSeconds.x86_64"; shift; fi
[ -x "$GAME" ] || { echo "No build at $GAME. Run Tools/unity.sh build-linux first." >&2; exit 1; }
args=(-screen-fullscreen 0 -screen-width 1600 -screen-height 900)
[ -n "${WAYLAND_DISPLAY:-}" ] && args+=(-force-wayland)
exec "$GAME" "${args[@]}" "$@"
