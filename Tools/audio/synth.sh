#!/usr/bin/env bash
# Generates all game audio with numpy. The system Python has no numpy, so this uses the
# interpreter bundled with Blender (override with PYTHON=...).
#   Tools/audio/synth.sh [sfx|music|<name>...]
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -z "${PYTHON:-}" ]; then
  BL="$(dirname "$(readlink -f "$(command -v blender)")")"
  PYTHON="$(ls "$BL"/*/python/bin/python3* 2>/dev/null | head -1)"
fi
exec "$PYTHON" "$DIR/synth.py" "$@"
