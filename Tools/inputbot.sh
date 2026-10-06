#!/usr/bin/env bash
# Controls smoke test: the built game plays 1-1 through virtual keyboard + mouse devices
# (taps, a held key, Shift-focus aiming, hover and click-to-borrow), then again on a virtual
# gamepad, which also drives the hint toggle, pause menu and Watch solution. Prints PASS/FAIL.
#   Tools/inputbot.sh [dir] [dev]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$ROOT/Builds/inputbot}"
MODE=(); [ "${2:-}" = "dev" ] && MODE=(dev)
rm -rf "$OUT"; mkdir -p "$OUT"
timeout 180 "$ROOT/Tools/play.sh" "${MODE[@]}" -logFile "$OUT/player.log" -bsInputBot "$OUT" > /dev/null 2>&1 || true
grep -v "^info tap" "$OUT/inputbot.log" 2>/dev/null || echo "no inputbot.log"
grep -q "^RESULT PASS" "$OUT/inputbot.log"
