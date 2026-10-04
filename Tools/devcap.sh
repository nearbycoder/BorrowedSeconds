#!/usr/bin/env bash
# Dev loop: incremental development build + autopilot capture.  Tools/devcap.sh DIR [player args]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$1"; shift
timeout 1200 "$ROOT/Tools/unity.sh" batch BorrowedSeconds.EditorTools.BuildScript.BuildLinuxDev /tmp/bs-build.log > /dev/null || true
grep -E "error CS|\[Build\]" /tmp/bs-build.log | head -20
"$ROOT/Tools/capture.sh" "$OUT" dev "$@"
