#!/usr/bin/env bash
# Plays Tools/trailer/shots.json in the release build and records one clip per shot.
#   Tools/trailer/record.sh                  every shot -> $BS_TRAILER_DIR/clips/<name>.video.mp4 + .audio.log
#   Tools/trailer/record.sh --only a,b       re-record some shots
#   Tools/trailer/record.sh --stills DIR     the shots' "stills" as 1920x1080 PNGs (full HUD, no captions)
#   Tools/trailer/record.sh --fidelity N     Graphics fidelity step, 0 Low .. 3 Ultra (default: Ultra for
#                                            clips, High, the game's default look, for stills)
# Frame-locked at 60 fps, so the result doesn't depend on how fast this machine renders, and Ultra
# costs nothing but recording time. The game window opens in a private headless KWin
# (Tools/nested.sh) when KWin is installed, never on the desktop (BS_NESTED=0 opts out).
# BS_TRAILER_DIR sets the work folder (default Builds/Trailer).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="$(realpath -m "${BS_TRAILER_DIR:-$ROOT/Builds/Trailer}")"
GAME="$ROOT/Builds/Linux/BorrowedSeconds.x86_64"
[ -x "$GAME" ] || { echo "No build at $GAME. Run Tools/unity.sh build-linux first." >&2; exit 1; }
MODE=(-bsTrailer "$WORK/clips")
ONLY=()
FIDELITY=""
while [ $# -gt 0 ]; do
  case "$1" in
    --only) ONLY=(-bsOnly "$2"); shift 2 ;;
    --stills) MODE=(-bsStills "$(realpath -m "$2")"); shift 2 ;;
    --fidelity) FIDELITY="$2"; shift 2 ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
done
if [ -z "$FIDELITY" ]; then FIDELITY=3; [ "${MODE[0]}" = -bsStills ] && FIDELITY=2; fi
mkdir -p "$WORK"
# a stills run writes only PNGs and a log: cap files at 512 MB, as the other scripted tools do, so a
# player stuck logging in a loop can't fill the disk (clip recording writes long videos, so it isn't capped)
[ "${MODE[0]}" = -bsStills ] && ulimit -f 524288
# the private compositor at 1x scale with room to spare, so the window renders exactly 1920x1080
export BS_NESTED="${BS_NESTED:-1}" BS_NESTED_SIZE="${BS_NESTED_SIZE:-2560x1440}" BS_NESTED_SCALE="${BS_NESTED_SCALE:-1}"
export BS_SIZE=1920x1080
# scripted runs keep Unity's config (screen prefs, PlayerPrefs) out of ~/.config/unity3d
export BS_CONFIG="$WORK/config"
timeout 3600 "$ROOT/Tools/play.sh" -logFile "$WORK/player.log" -bsFidelity "$FIDELITY" "${MODE[@]}" \
  -bsShotList "$ROOT/Tools/trailer/shots.json" "${ONLY[@]}" > /dev/null 2>&1 \
  || { echo "game failed, see $WORK/player.log" >&2; exit 1; }
grep -E "\[Trailer\] (wrote|still)" "$WORK/player.log" | sed "s|$ROOT/||" || true
