#!/usr/bin/env bash
# Builds the browser version for GitHub Pages into Builds/Pages (gitignored): index.html at its root,
# a .nojekyll file, the third-party notices, and Build/ with Brotli files the loader can decompress
# itself (Pages sends no Content-Encoding header). Serve it under /BorrowedSeconds/ to test it as
# Pages will (Tools/check-pages.mjs).
#   Tools/build-pages.sh [log]        default log: Logs/build-pages.log
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG="$(realpath -m "${1:-$ROOT/Logs/build-pages.log}")"
SITE="$ROOT/Builds/Pages"
mkdir -p "$(dirname "$LOG")"
rm -rf "$SITE"
start=$(date +%s)
# the log is capped at 512 MB (a build stuck logging in a loop must not fill the disk); not with
# ulimit -f, which also stops the editor writing its own larger Library files
status=0
nice -n 10 "$ROOT/Tools/unity.sh" build-web - 2>&1 | head -c 536870912 > "$LOG" || status=$?
if [ "$status" != 0 ] || ! grep -q "\[Build\] WebGL Succeeded" "$LOG"; then
  grep -E "\[Build\]|error" "$LOG" | tail -20 >&2 || true
  echo "web build failed (see $LOG)" >&2
  exit 1
fi
[ -f "$SITE/index.html" ] || { echo "no index.html in $SITE (see $LOG)" >&2; exit 1; }
touch "$SITE/.nojekyll"
mkdir -p "$SITE/licenses"
cp "$ROOT/THIRD_PARTY_NOTICES.md" "$SITE/"
cp "$ROOT/Assets/Resources/Fonts/OFL-FiraSans.txt" "$SITE/licenses/"
cp "$ROOT/Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt" "$SITE/licenses/OFL-LiberationSans.txt"
# GitHub refuses files over 100 MB and warns over 50 MB
big=$(find "$SITE" -type f -size +50M)
[ -z "$big" ] || { echo "files over 50 MB:" >&2; echo "$big" >&2; exit 1; }
echo "built $SITE in $(( $(date +%s) - start )) s: $(du -sh "$SITE" | cut -f1) total"
find "$SITE" -type f -printf '%s\t%P\n' | sort -rn | head -6 | awk -F'\t' '{ printf "  %8.1f MB  %s\n", $1 / 1048576, $2 }'
