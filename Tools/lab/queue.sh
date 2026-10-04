#!/usr/bin/env bash
# Runs design sweeps one after another in the background-friendly way:
#   Tools/lab/queue.sh [--jobs N] d61 d62 ...   -> /tmp/sw_<design>.txt each
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
JOBS=6
if [ "${1:-}" = "--jobs" ]; then JOBS=$2; shift 2; fi
for d in "$@"; do
  python3 "$ROOT/Tools/lab/sweep.py" "$ROOT/Tools/lab/designs/$d.py" --jobs "$JOBS" > "/tmp/sw_$d.txt" 2>&1
  echo "$d: $(grep '^done' /tmp/sw_$d.txt)"
done
