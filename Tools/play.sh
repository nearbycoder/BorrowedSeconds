#!/usr/bin/env bash
# Runs the built game. On this machine the X11/XWayland path can hang at startup, so use
# Unity's native Wayland backend when a Wayland session is available (the same rule as the
# zip's BorrowedSeconds.sh; BS_X11=1 skips it).
#   Tools/play.sh [dev] [extra player args...]
#   BS_SIZE=2560x1080 Tools/play.sh   another window size (default 1600x900)
#   BS_CONFIG=DIR Tools/play.sh       keep Unity's config (PlayerPrefs, screen prefs) in DIR, not
#                                     ~/.config/unity3d: every scripted tool sets this
#   BS_NESTED=1 Tools/play.sh         run in a private headless KWin (Tools/nested.sh), not on the
#                                     desktop: every scripted tool sets this unless BS_NESTED=0
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# hand the whole command line (including "dev") to a copy of this script inside the compositor
if [ "${BS_NESTED:-0}" = 1 ] && [ -z "${BS_NESTED_SOCKET:-}" ] && command -v kwin_wayland > /dev/null; then
  exec "$ROOT/Tools/nested.sh" --dir "$(dirname "$(realpath -m "${BS_CONFIG:-$ROOT/Builds/play/config}")")/nested" -- "${BASH_SOURCE[0]}" "$@"
fi
GAME="$ROOT/Builds/Linux/BorrowedSeconds.x86_64"
if [ "${1:-}" = "dev" ]; then GAME="$ROOT/Builds/LinuxDev/BorrowedSeconds.x86_64"; shift; fi
[ -x "$GAME" ] || { echo "No build at $GAME. Run Tools/unity.sh build-linux first." >&2; exit 1; }
SIZE="${BS_SIZE:-1600x900}"
args=(-screen-fullscreen 0 -screen-width "${SIZE%x*}" -screen-height "${SIZE#*x}")
if [ -n "${BS_CONFIG:-}" ]; then mkdir -p "$BS_CONFIG"; export XDG_CONFIG_HOME="$(realpath "$BS_CONFIG")"; fi
[ -n "${WAYLAND_DISPLAY:-}" ] && [ -z "${BS_X11:-}" ] && args+=(-force-wayland)
# inside the private compositor, checks that need a real one (focus, fullscreen) may run
[ -n "${BS_NESTED_SOCKET:-}" ] && args+=(-bsNested)
exec "$GAME" "${args[@]}" "$@"
