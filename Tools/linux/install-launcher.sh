#!/bin/sh
# Borrowed Seconds: adds the game to your applications menu, with its icon. On Wayland desktops
# this is also what gives the game's window its icon: the desktop finds a window's icon through
# a launcher named after the window's app id (the executable's name). The entry runs
# BorrowedSeconds.sh, which picks Unity's native Wayland backend on a Wayland desktop.
#   ./install-launcher.sh               add the launcher (run it again if you move this folder)
#   ./install-launcher.sh --uninstall   remove it
set -eu
DIR="$(cd "$(dirname "$0")" && pwd)"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
FILE="$APPS/BorrowedSeconds.x86_64.desktop"
if [ "${1:-}" = "--uninstall" ]; then
  rm -f "$FILE"
  echo "Removed $FILE"
  exit 0
fi
[ -f "$DIR/BorrowedSeconds.x86_64" ] || { echo "Run this from the unzipped Borrowed Seconds folder." >&2; exit 1; }
chmod +x "$DIR/BorrowedSeconds.x86_64"
RUN="$DIR/BorrowedSeconds.x86_64"
if [ -f "$DIR/BorrowedSeconds.sh" ]; then chmod +x "$DIR/BorrowedSeconds.sh"; RUN="$DIR/BorrowedSeconds.sh"; fi
# Exec takes the path quoted, with " ` $ and \ escaped (doubled once more by the string rules)
EXEC="$(printf '%s' "$RUN" | sed -e 's/\\/\\\\\\\\/g' -e 's/["`$]/\\\\&/g' -e 's/%/%%/g')"
# Path and Icon are plain strings: only a backslash needs escaping
STR="$(printf '%s' "$DIR" | sed -e 's/\\/\\\\/g')"
mkdir -p "$APPS"
cat > "$FILE" <<EOF
[Desktop Entry]
Type=Application
Name=Borrowed Seconds
Comment=Solve compact puzzles by borrowing time from your future self
Exec="$EXEC"
Path=$STR
Icon=$STR/icon.png
Terminal=false
Categories=Game;LogicGame;
StartupWMClass=BorrowedSeconds.x86_64
EOF
command -v update-desktop-database > /dev/null 2>&1 && update-desktop-database "$APPS" 2> /dev/null || true
echo "Added $FILE"
