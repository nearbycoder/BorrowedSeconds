#!/usr/bin/env bash
# Runs the Unity 6.6 editor against this project.
#
# CachyOS/Arch ship libxml2.so.16 but the editor links libxml2.so.2. If the system lacks it
# (fix: `sudo pacman -S libxml2-legacy`), put a copy at Tools/.libs/libxml2.so.2 (gitignored).
#
#   Tools/unity.sh                 open the project in the editor (GUI)
#   Tools/unity.sh batch <Method>  run a static editor method in batch mode and quit
#   Tools/unity.sh build-linux     batch-build Builds/Linux/BorrowedSeconds.x86_64
#   Tools/unity.sh build-mac       batch-build Builds/macOS/BorrowedSeconds.app (universal, unsigned)
#   Tools/unity.sh build-windows   batch-build Builds/Windows/BorrowedSeconds.exe (needs Windows Build Support)
#   Tools/unity.sh build-web       batch-build the browser version into Builds/Pages (Tools/build-pages.sh wraps it)
set -euo pipefail
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.6.2f1/Editor/Unity}"
PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [ -f "$PROJECT/Tools/.libs/libxml2.so.2" ]; then
  export LD_LIBRARY_PATH="$PROJECT/Tools/.libs${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi
# Batch runs get their own config folder: the editor shares the game's PlayerPrefs file
# (~/.config/unity3d/Borrowed Seconds/...) and rewrites it from memory when it quits, which could
# undo progress saved by a game played during a build. Only the editor's own folder (licences,
# editor prefs) is linked back in. The GUI editor (open) keeps the real config.
sandbox() {
  local real="${XDG_CONFIG_HOME:-$HOME/.config}/unity3d/Unity" dir="$PROJECT/Builds/editor-config"
  mkdir -p "$dir/unity3d"
  [ -e "$dir/unity3d/Unity" ] || { [ -d "$real" ] && ln -s "$real" "$dir/unity3d/Unity"; }
  export XDG_CONFIG_HOME="$dir"
}
[ "${1:-open}" = open ] || sandbox
case "${1:-open}" in
  open)
    exec "$UNITY" -projectPath "$PROJECT"
    ;;
  batch)
    exec "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -executeMethod "$2" -logFile "${3:--}"
    ;;
  build-linux)
    exec "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
      -executeMethod BorrowedSeconds.EditorTools.BuildScript.BuildLinux -logFile "${2:--}"
    ;;
  build-mac)
    exec "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
      -executeMethod BorrowedSeconds.EditorTools.BuildScript.BuildMac -logFile "${2:--}"
    ;;
  build-windows)
    exec "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
      -executeMethod BorrowedSeconds.EditorTools.BuildScript.BuildWindows -logFile "${2:--}"
    ;;
  build-web)
    # -buildTarget imports for the web up front; BuildWeb switches the editor back to Linux after
    exec "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -buildTarget WebGL \
      -executeMethod BorrowedSeconds.EditorTools.BuildScript.BuildWeb -logFile "${2:--}"
    ;;
  *)
    echo "usage: $0 [open|batch <Method> [log]|build-linux|build-mac|build-windows|build-web [log]]" >&2
    exit 2
    ;;
esac
