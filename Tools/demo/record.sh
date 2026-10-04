#!/usr/bin/env bash
# Records the demo reel from the release build (Tools/unity.sh build-linux first).
#   Tools/demo/record.sh [OUT.mp4]     default: Builds/Demo/BorrowedSeconds_demo.mp4
# The game plays a scripted reel frame-locked at 60 fps (muted) and pipes frames to ffmpeg;
# mix.py then rebuilds the soundtrack from its audio event log, and ffmpeg muxes the two.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="$(realpath -m "${1:-$ROOT/Builds/Demo/BorrowedSeconds_demo.mp4}")"
BASE="${OUT%.mp4}"
GAME="$ROOT/Builds/Linux/BorrowedSeconds.x86_64"
[ -x "$GAME" ] || { echo "No build at $GAME. Run Tools/unity.sh build-linux first." >&2; exit 1; }
if [ -z "${PYTHON:-}" ]; then
  BL="$(dirname "$(readlink -f "$(command -v blender)")")"
  PYTHON="$(ls "$BL"/*/python/bin/python3* 2>/dev/null | head -1)"
fi
mkdir -p "$(dirname "$OUT")"
args=(-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile "$BASE.player.log")
[ -n "${WAYLAND_DISPLAY:-}" ] && args+=(-force-wayland)
timeout 1800 "$GAME" "${args[@]}" -bsDemo "$BASE" > /dev/null 2>&1 || { echo "game failed, see $BASE.player.log" >&2; exit 1; }
"$PYTHON" "$ROOT/Tools/demo/mix.py" "$BASE.audio.log" "$BASE.audio.wav"
ffmpeg -y -loglevel error -i "$BASE.video.mp4" -i "$BASE.audio.wav" \
  -c:v copy -c:a aac -b:a 192k -shortest -movflags +faststart "$OUT"
rm -f "$BASE.video.mp4" "$BASE.audio.wav" "$BASE.audio.log"
echo "$OUT"
