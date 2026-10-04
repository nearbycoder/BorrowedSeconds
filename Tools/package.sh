#!/usr/bin/env bash
# Zips the release build for a GitHub release (Tools/unity.sh build-linux first).
#   Tools/package.sh 0.1.0   -> Builds/Release/BorrowedSeconds-v0.1.0-linux-x86_64.zip
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:?usage: Tools/package.sh VERSION}"
BUILD="$ROOT/Builds/Linux"
[ -x "$BUILD/BorrowedSeconds.x86_64" ] || { echo "No build in $BUILD. Run Tools/unity.sh build-linux first." >&2; exit 1; }
STAGE="$ROOT/Builds/Release/stage/BorrowedSeconds"
ZIP="$ROOT/Builds/Release/BorrowedSeconds-v$VERSION-linux-x86_64.zip"
rm -rf "$ROOT/Builds/Release/stage" "$ZIP"
mkdir -p "$STAGE/licenses"
# everything but Unity's "don't ship" folder (debug symbols for crash reports)
rsync -a --exclude '*_BackUpThisFolder_ButDontShipItWithYourGame' "$BUILD/" "$STAGE/"
cp "$ROOT/THIRD_PARTY_NOTICES.md" "$STAGE/"
cp "$ROOT/Assets/Resources/Fonts/OFL-FiraSans.txt" "$STAGE/licenses/"
cp "$ROOT/Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt" "$STAGE/licenses/OFL-LiberationSans.txt"
cat > "$STAGE/README.txt" <<EOF
Borrowed Seconds $VERSION for Linux (x86_64)
https://github.com/nearbycoder/BorrowedSeconds

Run:   ./BorrowedSeconds.x86_64
       (if it isn't executable: chmod +x BorrowedSeconds.x86_64)
       On Wayland, add -force-wayland if the game hangs at startup under XWayland.

Move: WASD / arrows / left stick        Borrow (freeze the aimed obstacle): click / Space / A
Aim: hover with the mouse, Tab / Q / E, LB / RB
Focus (slow time): hold Shift / right mouse / LT
Rewind: hold Z / Backspace / X          Restart: R / Y          Pause: Esc / P / Start

Progress and settings are saved automatically (Unity PlayerPrefs, ~/.config/unity3d/).
Third-party notices: THIRD_PARTY_NOTICES.md and licenses/.
EOF
# bsdtar's zip writer keeps the executable bits
(cd "$ROOT/Builds/Release/stage" && bsdtar --format zip --options zip:compression-level=9 -cf "$ZIP" BorrowedSeconds)
rm -rf "$ROOT/Builds/Release/stage"
ls -la "$ZIP"
