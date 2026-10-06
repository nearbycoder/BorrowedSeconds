#!/usr/bin/env bash
# Behaviour checks in the built game (dying and rewinding, menus, settings): things a solver
# replay never exercises. Prints PASS/FAIL per check.
#   Tools/checks.sh [dir] [dev]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-/tmp/bs-checks}"
MODE=(); [ "${2:-}" = "dev" ] && MODE=(dev)
rm -rf "$OUT"; mkdir -p "$OUT"
timeout 600 "$ROOT/Tools/play.sh" "${MODE[@]}" -logFile "$OUT/player.log" -bsChecks "$OUT" > /dev/null 2>&1 || true
cat "$OUT/checks.log" 2>/dev/null || echo "no checks.log"
grep -E "Exception" "$OUT/player.log" | head -10 || true
grep -q "^done fail=0" "$OUT/checks.log"
