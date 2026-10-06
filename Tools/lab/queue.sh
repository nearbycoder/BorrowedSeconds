#!/usr/bin/env bash
# Runs design sweeps one after another in the background-friendly way:
#   Tools/lab/queue.sh [--jobs N] d61 d62 ...   -> Builds/sweeps/<design>.txt each
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
JOBS=6
mkdir -p "$ROOT/Builds/sweeps"
if [ "${1:-}" = "--jobs" ]; then JOBS=$2; shift 2; fi
for d in "$@"; do
  python3 "$ROOT/Tools/lab/sweep.py" "$ROOT/Tools/lab/designs/$d.py" --jobs "$JOBS" > "$ROOT/Builds/sweeps/$d.txt" 2>&1
  echo "$d: $(grep "^done" "$ROOT/Builds/sweeps/$d.txt")"
done
