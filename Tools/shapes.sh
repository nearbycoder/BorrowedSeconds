#!/usr/bin/env bash
# Layout check across screen shapes: the built game runs the menu tour (-bsMenus) and plays one
# level (-bsCapture) in a window of each size, then a contact sheet per size is made for review.
#   Tools/shapes.sh [dir] [dev] [sizes...]    default sizes: 2560x1080 1600x900 1280x800 1024x768
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$(realpath -m "${1:-$ROOT/Builds/shapes}")"; shift || true
MODE=(); if [ "${1:-}" = "dev" ]; then MODE=(dev); shift; fi
SIZES=("$@"); [ ${#SIZES[@]} -gt 0 ] || SIZES=(2560x1080 1600x900 1280x800 1024x768)
LEVEL="${BS_LEVEL:-4-5}"
rm -rf "$OUT"; mkdir -p "$OUT"
ulimit -f 524288
for size in "${SIZES[@]}"; do
  D="$OUT/$size"; mkdir -p "$D/menus" "$D/level"
  BS_SIZE="$size" BS_CONFIG="$D/config" timeout 300 "$ROOT/Tools/play.sh" "${MODE[@]}" -logFile "$D/menus.log" -bsMenus "$D/menus" > /dev/null 2>&1 || true
  BS_SIZE="$size" BS_CONFIG="$D/config" timeout 300 "$ROOT/Tools/play.sh" "${MODE[@]}" -logFile "$D/level.log" -bsCapture "$D/level" -bsOnly "$LEVEL" -bsShots 70,200 > /dev/null 2>&1 || true
  shots=()
  for f in menus/01_title_320 menus/03_levels_160 menus/04_card_260 menus/06_pause_100 menus/07_settings_100 menus/08_complete_260; do
    [ -f "$D/$f.png" ] && shots+=("$D/$f.png")
  done
  shots+=($(ls "$D"/level/"$LEVEL"_t*.png 2>/dev/null | head -2))
  if [ ${#shots[@]} -gt 0 ]; then
    magick montage "${shots[@]}" -tile 4x2 -geometry 960x+6+6 -background '#111' "$OUT/sheet_$size.jpg"
  fi
  echo "$size: ${#shots[@]} shots, $(grep -c PASS "$D/level/autopilot.log" 2>/dev/null || echo 0) level pass"
done
