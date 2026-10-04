# Trailer and README media

```
Tools/unity.sh build-linux              # the release build the trailer is recorded from
Tools/trailer/make_trailer.sh           # record every shot, cut, score, encode, poster, teaser, screenshots
Tools/trailer/make_trailer.sh --cut     # re-cut and re-score the clips already recorded
Tools/trailer/make_trailer.sh --stills  # screenshots only
Tools/trailer/record.sh --only a,b      # re-record some shots
```

Outputs: `docs/media/trailer.mp4`, `trailer-poster.jpg`, `teaser.webp` and `screenshots/*.png`.
Intermediate clips, logs and stems go to `Builds/Trailer/` (gitignored). The scripts need
ffmpeg, ImageMagick and Blender (its bundled Python provides numpy).

## How it works

1. **Recording** (`record.sh`). The release build runs with `-bsTrailer DIR -bsShotList
   shots.json` (`Assets/Scripts/Game/GameRoot.Trailer.cs`). For each shot it loads a level, drives
   it with the solver's saved replay, and records one clip frame-locked at 60 fps
   (`Time.captureFramerate`, raw frames piped to ffmpeg). Every sound the game would have played
   goes to the clip's `.audio.log`. Captions, the stamp band, and the title and end cards are drawn
   in game with the same UI kit as the menus. `-bsStills DIR` plays the same shots with the full HUD
   and no captions, and saves the shots' `stills` as PNGs.
2. **Cutting and scoring** (`compose.py`). The clips are joined with ffmpeg `xfade` transitions
   (`radial` is a clock-hand sweep). The soundtrack is rebuilt from the logs: every effect at its
   exact frame, with the game's frozen muffle on world sounds. The music bed is cut from the game's
   own 96 BPM loops, and each section starts on a downbeat. While the player is frozen, the music is
   muffled the same way the game does it, and it ducks under the effects. Then two-pass loudness
   normalisation brings it to -16 LUFS, and a two-pass x264 encode fits the size budget (36 MB by
   default).

## Shot keys (`shots.json`)

| Key | Meaning |
|---|---|
| `name` | Clip name (`Builds/Trailer/clips/<name>.video.mp4`). |
| `type` | `level` (default), `chapter` (chapter card, then the level), `title` / `end` (logo cards), `ledger` (level select), `titlescreen` / `ending` (the real screens, for stills). |
| `level`, `seek`, `shift`, `fail` | Level id; the tick to start at (fast-forwarded silently); a delay added to every replay action (only values the solver accepts, so the replay still wins); `fail: true` uses a variant that defaults on thawing. |
| `dur` / `until` / `win` / `death`, `hold` | Clip length in seconds, or end when a tick is reached, the level is won, or a default has rewound; then `hold` seconds more. |
| `aim`, `aimLead`, `focus`, `slow` | `[[from, to, obstacle]]` forced aim windows (otherwise aim `aimLead` ticks ahead of each scripted borrow), `[[from, to]]` Focus windows, `[[from, to, speed]]` slow motion (all in ticks). |
| `zoom`, `zoomTo`, `zoomTime`, `shiftX`, `lean`, `pan` | Camera: distance, a push-in over time, a sideways shift, a lean toward `[x, y, amount]`, a world pan `[x, z]`. |
| `captions` | `{at or tick, out or outTick, pos: tl/tr/bl/br, kicker, head, sub, wrap}` caption cards. |
| `stamps` | `{at, text, sub, band: "hold"}` stamped titles in a band across the top. |
| `events` | `{at, do: pause / settings / select / adjust, arg}` scripted menu moves. |
| `prompts`, `learned`, `hud`, `muted`, `blur`, `keep` | Show the onboarding prompts (with `learned` bits already retired), show the HUD, silence the board, blur it, keep captions up to the cut. |
| `stills` | `{at or tick, name}` screenshots for `--stills`. |
| `transition`, `xfade` | The cut into this shot: any ffmpeg `xfade` transition, and its length (`0` is a hard cut). |
| `music`, `musicGain`, `musicLowpass`, `sfx` | Start a music section on this shot (`music_title`, `music_a`, `music_b` or `music_finale`); extra one-shots `[[time, clip, gain]]`. |
