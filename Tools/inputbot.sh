#!/usr/bin/env bash
# Controls smoke test: the built game plays 1-1 through virtual keyboard + mouse devices
# (taps, a held key, Shift-focus aiming, hover and click-to-borrow), then again on a virtual
# gamepad, which also drives the hint toggle, pause menu and Watch solution. Prints PASS/FAIL.
#   Tools/inputbot.sh [dir] [dev]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$(realpath -m "${1:-$ROOT/Builds/inputbot}")"
MODE=(); [ "${2:-}" = "dev" ] && MODE=(dev)
rm -rf "$OUT"; mkdir -p "$OUT"
ulimit -f 524288 # 512 MB per file: a player stuck logging in a loop must not fill the disk
export BS_NESTED="${BS_NESTED:-1}" # the game window opens in a private headless KWin (Tools/nested.sh), not on the desktop
BS_CONFIG="${BS_CONFIG:-$OUT/config}" timeout 300 "$ROOT/Tools/play.sh" "${MODE[@]}" -logFile "$OUT/player.log" -bsInputBot "$OUT" > /dev/null 2>&1 || true
grep -v "^info tap" "$OUT/inputbot.log" 2>/dev/null || echo "no inputbot.log"
grep -q "^RESULT PASS" "$OUT/inputbot.log"
