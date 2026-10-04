#!/usr/bin/env bash
# Makes the trailer and the README media from the release build (Tools/unity.sh build-linux first).
#   Tools/trailer/make_trailer.sh             record every shot, cut, score, encode, then poster,
#                                             teaser loop and screenshots
#   Tools/trailer/make_trailer.sh --cut       re-cut from the clips already in Builds/Trailer/clips
#   Tools/trailer/make_trailer.sh --stills    screenshots only
# Writes docs/media/trailer.mp4, trailer-poster.jpg, teaser.webp and screenshots/*.png.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HERE="$ROOT/Tools/trailer"
MEDIA="$ROOT/docs/media"
WORK="$ROOT/Builds/Trailer"
FONT="$ROOT/Assets/Resources/Fonts/FiraSans-SemiBold.ttf"
if [ -z "${PYTHON:-}" ]; then
  BL="$(dirname "$(readlink -f "$(command -v blender)")")"
  PYTHON="$(ls "$BL"/*/python/bin/python3* 2>/dev/null | head -1)" # Blender's Python ships numpy
fi
mkdir -p "$MEDIA/screenshots" "$WORK"
MODE="${1:-all}"

stills() {
  rm -rf "$WORK/stills"
  "$HERE/record.sh" --stills "$WORK/stills"
  find "$MEDIA/screenshots" -name '*.png' -delete
  for png in "$WORK/stills/"*.png; do
    # lossless; these dark scenes stay around 1 MB at 1920x1080
    magick "$png" -strip -define png:compression-level=9 "$MEDIA/screenshots/$(basename "$png")"
  done
  ls -la "$MEDIA/screenshots"
}

if [ "$MODE" = "--stills" ]; then stills; exit 0; fi
[ "$MODE" = "--cut" ] || "$HERE/record.sh"
"$PYTHON" "$HERE/compose.py" --out "$MEDIA/trailer.mp4"

# poster: the title card with a play button, linked from the README
start=$(python3 -c "import json;print(next(c['start'] for c in json.load(open('$WORK/timeline.json')) if c['name']=='title'))")
ffmpeg -y -v error -ss "$(python3 -c "print($start + 3.9)")" -i "$MEDIA/trailer.mp4" -frames:v 1 "$WORK/poster.png"
dur=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$MEDIA/trailer.mp4")
len=$(python3 -c "d=round($dur); print(f'{d//60}:{d%60:02d}')")
magick "$WORK/poster.png" \
  \( -size 1920x1080 xc:none -fill "rgba(4,6,16,0.6)" -draw "circle 960,880 960,798" \
     -fill none -stroke "rgba(255,210,122,0.95)" -strokewidth 5 -draw "circle 960,880 960,798" \
     -stroke none -fill "rgba(246,241,231,0.97)" -draw "polygon 940,840 940,920 1004,880" \) -composite \
  -font "$FONT" -pointsize 34 -fill "rgba(246,241,231,0.95)" -gravity north -annotate +0+985 "WATCH THE TRAILER  ·  $len" \
  -resize 1280x720 -strip -quality 88 "$MEDIA/trailer-poster.jpg"

# teaser: the cold open as a seamless 960x540 loop (its last half second dissolves into its first)
co="$WORK/clips/coldopen.video.mp4"
d=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$co")
ffmpeg -y -v error -i "$co" -filter_complex \
  "[0:v]fps=25,scale=960:540:flags=lanczos,split[a][b];[a]trim=start=0.5,setpts=PTS-STARTPTS[main];[b]trim=0:0.5,setpts=PTS-STARTPTS[head];[main][head]xfade=transition=fade:duration=0.5:offset=$(python3 -c "print(round($d - 1.0, 3))")" \
  -c:v libwebp_anim -lossless 0 -quality 72 -compression_level 6 -loop 0 -an "$MEDIA/teaser.webp"

[ "$MODE" = "--cut" ] || stills
ls -la "$MEDIA" | sed "s|$ROOT/||"
