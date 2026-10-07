#!/usr/bin/env bash
# Runs a command inside a private, headless KWin (kwin_wayland --virtual), so a test window
# never opens on the real desktop and can be focused, unfocused, made fullscreen or scaled
# without touching anyone else's windows. KWin gets its own Wayland socket, D-Bus session and
# config, cache and data folders (it writes kwinrc and kwinoutputconfig.json, which must not
# land in ~/.config); the command sees WAYLAND_DISPLAY set to the private socket and no DISPLAY
# (with --xwayland, KWin's own Xwayland). Exits with the command's status.
#   Tools/nested.sh [--size WxH] [--scale S] [--xwayland] [--dir DIR] -- command [args...]
#   defaults: 3840x2160 at scale 1.25 (the development desktop: 4K at 125 %, so 3072x1728 logical),
#   DIR Builds/nested. The scale is set with kscreen-doctor once KWin is up.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SIZE="${BS_NESTED_SIZE:-3840x2160}"
SCALE="${BS_NESTED_SCALE:-1.25}"
DIR=""
XW=(); [ -n "${BS_NESTED_XWAYLAND:-}" ] && XW=(--xwayland)
while [ $# -gt 0 ]; do
  case "$1" in
    --size) SIZE="$2"; shift 2 ;;
    --scale) SCALE="$2"; shift 2 ;;
    --xwayland) XW=(--xwayland); shift ;;
    --dir) DIR="$2"; shift 2 ;;
    --) shift; break ;;
    *) break ;;
  esac
done
[ $# -gt 0 ] || { echo "usage: Tools/nested.sh [--size WxH] [--scale S] [--xwayland] [--dir DIR] -- command [args...]" >&2; exit 2; }
command -v kwin_wayland > /dev/null || { echo "nested.sh: kwin_wayland is not installed" >&2; exit 3; }
command -v dbus-run-session > /dev/null || { echo "nested.sh: dbus-run-session is not installed" >&2; exit 3; }
DIR="$(realpath -m "${DIR:-$ROOT/Builds/nested}")"
mkdir -p "$DIR/config" "$DIR/cache" "$DIR/data"
rm -f "$DIR/config/kwinoutputconfig.json" # KWin would bring back the last run's output size and scale
SOCKET="bs-nested-$$"
STATUS="$DIR/status.$$"
rm -f "$STATUS"

# the session's one application: the command, its status written where this script can read it
# (KWin's own exit status doesn't carry it)
INNER="$DIR/inner.$$.sh"
{
  echo '#!/usr/bin/env bash'
  printf 'export XDG_CONFIG_HOME=%q XDG_CACHE_HOME=%q XDG_DATA_HOME=%q\n' "${XDG_CONFIG_HOME:-$HOME/.config}" "${XDG_CACHE_HOME:-$HOME/.cache}" "${XDG_DATA_HOME:-$HOME/.local/share}"
  printf 'export BS_NESTED_SOCKET=%q\n' "$SOCKET"
  # KWin's --scale only multiplies a virtual output's size; a real (fractional) output scale is
  # set from inside the session, as the desktop's display settings would
  if [ "$SCALE" != 1 ]; then
    echo 'out="$(kscreen-doctor -o 2>/dev/null | sed "s/\x1b\[[0-9;]*m//g" | awk "/^Output:/ {print \$3; exit}")"'
    printf '[ -n "$out" ] && kscreen-doctor "output.$out.scale.%s" > /dev/null 2>&1\n' "$SCALE"
  fi
  printf '%q ' "$@"; echo
  printf 'echo $? > %q\n' "$STATUS"
} > "$INNER"
chmod +x "$INNER"

# the command keeps the caller's XDG folders (restored above); only KWin and its D-Bus get the private ones
# in a process group of its own, so stopping this script stops KWin, its D-Bus and the command
setsid env -u DISPLAY -u WAYLAND_DISPLAY \
  XDG_CONFIG_HOME="$DIR/config" XDG_CACHE_HOME="$DIR/cache" XDG_DATA_HOME="$DIR/data" \
  dbus-run-session -- kwin_wayland --virtual --socket "$SOCKET" --width "${SIZE%x*}" --height "${SIZE#*x}" \
    --no-lockscreen --no-global-shortcuts --no-kactivities "${XW[@]}" --exit-with-session "$INNER" \
  >> "$DIR/kwin.log" 2>&1 &
KPID=$!
trap 'kill -TERM -- "-$KPID" 2> /dev/null || true' TERM INT
wait "$KPID" || true
trap - TERM INT
rm -f "$INNER"
code=1
[ -f "$STATUS" ] && code="$(cat "$STATUS")" && rm -f "$STATUS"
exit "$code"
