#!/usr/bin/env bash
# Behaviour checks in the built game (dying and rewinding, menus, settings): things a solver
# replay never exercises. Prints PASS/FAIL per check.
#   Tools/checks.sh [dir] [dev] [-bsOnly name1,name2]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$(realpath -m "${1:-$ROOT/Builds/checks}")"
MODE=(); [ "${2:-}" = "dev" ] && MODE=(dev)
EXTRA=("${@:3}")
rm -rf "$OUT"; mkdir -p "$OUT"
ulimit -f 524288 # 512 MB per file: a player stuck logging in a loop must not fill the disk
BS_CONFIG="${BS_CONFIG:-$OUT/config}" timeout 600 "$ROOT/Tools/play.sh" "${MODE[@]}" -logFile "$OUT/player.log" -bsChecks "$OUT" "${EXTRA[@]}" > /dev/null 2>&1 || true
cat "$OUT/checks.log" 2>/dev/null || echo "no checks.log"
grep -E "Exception" "$OUT/player.log" | head -10 || true
# the audio log is judged offline, by the same mixer that builds the demo soundtracks
# (numpy: the system Python has none, so use Blender's bundled interpreter unless PYTHON is set)
if [ -z "${PYTHON:-}" ]; then
  BL="$(dirname "$(readlink -f "$(command -v blender)")")"
  PYTHON="$(ls "$BL"/*/python/bin/python3* 2>/dev/null | head -1)"
fi
BALANCE=0
if [ -f "$OUT/audio.log" ]; then "$PYTHON" "$ROOT/Tools/audio/balance.py" "$OUT/audio.log" | tee "$OUT/balance.txt" | tail -3 || BALANCE=1; fi
grep -q "^done fail=0" "$OUT/checks.log" && [ "$BALANCE" = 0 ]
