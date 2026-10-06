#!/usr/bin/env bash
# Dev loop: incremental development build + autopilot capture.  Tools/devcap.sh DIR [player args]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$1"; shift
timeout 1200 "$ROOT/Tools/unity.sh" batch BorrowedSeconds.EditorTools.BuildScript.BuildLinuxDev "$ROOT/Builds/build-dev.log" > /dev/null || true
grep -E "error CS|\[Build\]" "$ROOT/Builds/build-dev.log" | sort -u | head -20; grep -q "\[Build\].*Succeeded" "$ROOT/Builds/build-dev.log" || { echo "BUILD FAILED"; exit 1; }
"$ROOT/Tools/capture.sh" "$OUT" dev "$@"
