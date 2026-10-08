#!/usr/bin/env bash
# Graphics fidelity, step by step: screenshots of one held frame at Low, Medium, High and Ultra
# (4-5, 2-3 and 3-5), then frame times at every step on 4-5, 3-5 and 2-4 with vsync off, each
# step measured twice (Low to Ultra, then back). Writes DIR/fidelity.log and DIR/*.png.
#   Tools/fidelity.sh [dir] [dev]        BS_SIZE=WxH sets the window (default 1920x1080)
# The GPU is shared on the development machine: note the load (printed first) with any numbers.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$(realpath -m "${1:-$ROOT/Builds/fidelity}")"; shift || true
MODE=(); [ "${1:-}" = "dev" ] && MODE=(dev)
rm -rf "$OUT"; mkdir -p "$OUT"
ulimit -f 524288 # 512 MB per file: a player stuck logging in a loop must not fill the disk
export BS_NESTED="${BS_NESTED:-1}" # the game window opens in a private headless KWin (Tools/nested.sh), not on the desktop
export BS_SIZE="${BS_SIZE:-1920x1080}"
echo "load before: $(cut -d' ' -f1-3 /proc/loadavg)" | tee "$OUT/load.txt"
BS_CONFIG="${BS_CONFIG:-$OUT/config}" timeout 600 "$ROOT/Tools/play.sh" "${MODE[@]}" -logFile "$OUT/player.log" -bsFidelityShots "$OUT" -bsNoVsync > /dev/null 2>&1 || true
echo "load after: $(cut -d' ' -f1-3 /proc/loadavg)" | tee -a "$OUT/load.txt"
cat "$OUT/fidelity.log" 2>/dev/null || echo "no fidelity.log"
grep -E "Exception" "$OUT/player.log" | head -10 || true
