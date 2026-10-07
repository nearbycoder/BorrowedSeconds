#!/bin/sh
# Borrowed Seconds launcher: ships next to BorrowedSeconds.x86_64 in the Linux zip.
# On a Wayland desktop Unity's player picks its X11 backend (XWayland), which can hang at startup
# before the window opens, so this starts it with Unity's native Wayland backend instead.
#   ./BorrowedSeconds.sh            play
#   BS_X11=1 ./BorrowedSeconds.sh   use X11/XWayland anyway
# Other arguments go to the game (for example -screen-fullscreen 0 for a window).
DIR="$(cd "$(dirname "$0")" && pwd)"
if [ -n "${WAYLAND_DISPLAY:-}" ] && [ -z "${BS_X11:-}" ]; then
  case " $* " in
    *" -force-wayland "*) ;;
    *) set -- -force-wayland "$@" ;;
  esac
fi
exec "$DIR/BorrowedSeconds.x86_64" "$@"
