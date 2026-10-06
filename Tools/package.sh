#!/usr/bin/env bash
# Zips a release build for a GitHub release (Tools/unity.sh build-linux / build-mac first).
#   Tools/package.sh 0.1.0         -> Builds/Release/BorrowedSeconds-v0.1.0-linux-x86_64.zip
#   Tools/package.sh 0.1.0 macos   -> Builds/Release/BorrowedSeconds-v0.1.0-macos-universal.zip (unsigned)
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:?usage: Tools/package.sh VERSION [linux|macos]}"
PLATFORM="${2:-linux}"
STAGE="$ROOT/Builds/Release/stage/BorrowedSeconds"
CONTROLS="Move: WASD / arrows / left stick        Borrow (freeze the aimed obstacle): click / Space / A
Aim: hover with the mouse, Tab / Q / E, LB / RB
Focus (slow time): hold Shift / right mouse / LT
Rewind: hold Z / Backspace / X          Restart: R / Y          Pause: Esc / P / Start
Hint: H / Select                        Stuck? Pause > Watch solution"

case "$PLATFORM" in
  linux)
    BUILD="$ROOT/Builds/Linux"
    [ -x "$BUILD/BorrowedSeconds.x86_64" ] || { echo "No build in $BUILD. Run Tools/unity.sh build-linux first." >&2; exit 1; }
    ZIP="$ROOT/Builds/Release/BorrowedSeconds-v$VERSION-linux-x86_64.zip"
    ;;
  macos)
    BUILD="$ROOT/Builds/macOS"
    [ -d "$BUILD/BorrowedSeconds.app" ] || { echo "No build in $BUILD. Run Tools/unity.sh build-mac first." >&2; exit 1; }
    ZIP="$ROOT/Builds/Release/BorrowedSeconds-v$VERSION-macos-universal.zip"
    ;;
  *)
    echo "unknown platform: $PLATFORM (linux or macos)" >&2; exit 2
    ;;
esac
rm -rf "$ROOT/Builds/Release/stage" "$ZIP"
mkdir -p "$STAGE/licenses"
# everything but Unity's "don't ship" folder (debug symbols for crash reports)
rsync -a --exclude '*_BackUpThisFolder_ButDontShipItWithYourGame' "$BUILD/" "$STAGE/"
cp "$ROOT/THIRD_PARTY_NOTICES.md" "$STAGE/"
cp "$ROOT/Assets/Resources/Fonts/OFL-FiraSans.txt" "$STAGE/licenses/"
cp "$ROOT/Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt" "$STAGE/licenses/OFL-LiberationSans.txt"
if [ "$PLATFORM" = linux ]; then
cat > "$STAGE/README.txt" <<EOF
Borrowed Seconds $VERSION for Linux (x86_64)
https://github.com/nearbycoder/BorrowedSeconds

Run:   ./BorrowedSeconds.x86_64
       (if it isn't executable: chmod +x BorrowedSeconds.x86_64)
       On Wayland, add -force-wayland if the game hangs at startup under XWayland.

$CONTROLS

Progress and settings are saved automatically (Unity PlayerPrefs, ~/.config/unity3d/).
Third-party notices: THIRD_PARTY_NOTICES.md and licenses/.
EOF
else
cat > "$STAGE/README.txt" <<EOF
Borrowed Seconds $VERSION for macOS (universal: Intel and Apple silicon)
https://github.com/nearbycoder/BorrowedSeconds

This build is only ad-hoc signed (no Apple Developer ID) and NOT notarized, and it was built
on Linux without being tested on a Mac.
macOS will refuse to open it at first. To run it anyway:
  1. Move BorrowedSeconds.app wherever you like (e.g. Applications).
  2. In Terminal:  xattr -dr com.apple.quarantine /path/to/BorrowedSeconds.app
     (or Control-click the app, choose Open, then Open again; on recent macOS you may need
     System Settings > Privacy & Security > Open Anyway).

$CONTROLS

Progress and settings are saved automatically (Unity PlayerPrefs, ~/Library/Preferences/).
Third-party notices: THIRD_PARTY_NOTICES.md and licenses/.
EOF
fi
# bsdtar's zip writer keeps the executable bits (and the app bundle's symlinks)
(cd "$ROOT/Builds/Release/stage" && bsdtar --format zip --options zip:compression-level=9 -cf "$ZIP" BorrowedSeconds)
rm -rf "$ROOT/Builds/Release/stage"
ls -la "$ZIP"
