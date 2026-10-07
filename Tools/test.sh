#!/usr/bin/env bash
# EditMode tests in a batch editor, with the project's persistentDataPath kept out of
# ~/.config/unity3d (the test runner always saves a copy of its results there).
#   Tools/test.sh [dir] [--filter pattern]     default dir: Builds/tests
# The editor's own config (licences, editor prefs) stays shared: only unity3d/Unity is linked
# into the sandbox, so "Borrowed Seconds/Borrowed Seconds/TestResults.xml" lands in DIR instead.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$(realpath -m "${1:-$ROOT/Builds/tests}")"; shift || true
FILTER=()
[ "${1:-}" = "--filter" ] && FILTER=(-testFilter "$2")
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.6.2f1/Editor/Unity}"
REAL="${XDG_CONFIG_HOME:-$HOME/.config}/unity3d/Unity"
rm -rf "$OUT"; mkdir -p "$OUT/config/unity3d"
[ -d "$REAL" ] && ln -s "$REAL" "$OUT/config/unity3d/Unity"
if [ -f "$ROOT/Tools/.libs/libxml2.so.2" ]; then
  export LD_LIBRARY_PATH="$ROOT/Tools/.libs${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi
XDG_CONFIG_HOME="$OUT/config" timeout 1800 "$UNITY" -batchmode -nographics -projectPath "$ROOT" \
  -runTests -testPlatform EditMode -testResults "$OUT/results.xml" "${FILTER[@]}" -logFile "$OUT/editor.log" > /dev/null 2>&1 || true
[ -f "$OUT/results.xml" ] || { echo "no results (see $OUT/editor.log)"; exit 1; }
python3 - "$OUT/results.xml" <<'EOF'
import sys, xml.etree.ElementTree as ET
run = ET.parse(sys.argv[1]).getroot()
a = run.attrib
print(f"tests {a.get('total')}  passed {a.get('passed')}  failed {a.get('failed')}  skipped {a.get('skipped')}  ({float(a.get('duration', 0)):.0f} s)")
for tc in run.iter('test-case'):
    if tc.get('result') == 'Failed':
        print("FAIL", tc.get('fullname'))
sys.exit(0 if a.get('result', '').startswith('Passed') else 1)
EOF
