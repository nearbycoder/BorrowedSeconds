#!/usr/bin/env bash
# Self-test: the built game plays every level from the solver's replays, saves screenshots to
# ${1:-/tmp/bs-capture} and prints PASS/FAIL per level.
#   Tools/capture.sh [dir] [dev] [-bsOnly 1-1] [-bsShots 10,40]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-/tmp/bs-capture}"; shift || true
MODE=()
if [ "${1:-}" = "dev" ]; then MODE=(dev); shift; fi
rm -rf "$OUT"; mkdir -p "$OUT"
ulimit -f 524288 # 512 MB per file: a player stuck logging in a loop must not fill the disk
timeout 2400 "$ROOT/Tools/play.sh" "${MODE[@]}" -logFile "$OUT/player.log" -bsCapture "$OUT" "$@" > /dev/null 2>&1 || true
cat "$OUT/autopilot.log" 2>/dev/null || echo "no autopilot.log"
grep -E "Exception|Error" "$OUT/player.log" | grep -v "^Fallback" | head -20 || true
