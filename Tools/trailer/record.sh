#!/usr/bin/env bash
# Plays Tools/trailer/shots.json in the release build and records one clip per shot.
#   Tools/trailer/record.sh                  every shot -> Builds/Trailer/clips/<name>.video.mp4 + .audio.log
#   Tools/trailer/record.sh --only a,b       re-record some shots
#   Tools/trailer/record.sh --stills DIR     the shots' "stills" as 1920x1080 PNGs (full HUD, no captions)
# Frame-locked at 60 fps, so the result doesn't depend on how fast this machine renders.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
GAME="$ROOT/Builds/Linux/BorrowedSeconds.x86_64"
[ -x "$GAME" ] || { echo "No build at $GAME. Run Tools/unity.sh build-linux first." >&2; exit 1; }
MODE=(-bsTrailer "$ROOT/Builds/Trailer/clips")
ONLY=()
while [ $# -gt 0 ]; do
  case "$1" in
    --only) ONLY=(-bsOnly "$2"); shift 2 ;;
    --stills) MODE=(-bsStills "$(realpath -m "$2")"); shift 2 ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
done
mkdir -p "$ROOT/Builds/Trailer"
# a stills run writes only PNGs and a log: cap files at 512 MB, as the other scripted tools do, so a
# player stuck logging in a loop can't fill the disk (clip recording writes long videos, so it isn't capped)
[ "${MODE[0]}" = -bsStills ] && ulimit -f 524288
args=(-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile "$ROOT/Builds/Trailer/player.log")
[ -n "${WAYLAND_DISPLAY:-}" ] && args+=(-force-wayland)
# scripted runs keep Unity's config (screen prefs, PlayerPrefs) out of ~/.config/unity3d
mkdir -p "$ROOT/Builds/Trailer/config"; export XDG_CONFIG_HOME="$ROOT/Builds/Trailer/config"
timeout 3600 "$GAME" "${args[@]}" "${MODE[@]}" -bsShotList "$ROOT/Tools/trailer/shots.json" "${ONLY[@]}" > /dev/null 2>&1 \
  || { echo "game failed, see Builds/Trailer/player.log" >&2; exit 1; }
grep -E "\[Trailer\] (wrote|still)" "$ROOT/Builds/Trailer/player.log" | sed "s|$ROOT/||" || true
