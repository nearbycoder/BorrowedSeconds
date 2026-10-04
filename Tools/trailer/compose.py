"""Cuts the recorded trailer clips together, scores them and encodes the finished trailer.

    compose.py [--out docs/media/trailer.mp4] [--mb 36]

Reads Tools/trailer/shots.json for the edit (order, transitions, music, extra sound effects) and
Builds/Trailer/clips/<shot>.video.mp4 + <shot>.audio.log for the pictures and the game's own sound
events (Tools/trailer/record.sh makes them). The soundtrack is rebuilt offline:

* effects: every one-shot the game played in each clip, at its exact frame, with the in-game
  "frozen" muffle on world sounds (the same mixer as Tools/demo/mix.py);
* music: the game's own 96 BPM loops, each section starting on a downbeat where a shot names a
  new "music" track, muffled while the player is frozen (as in game) and ducked under the effects;
* master: fades, then loudness-normalised by ffmpeg to -16 LUFS.

Pictures are joined with ffmpeg xfade transitions ("radial" is a clock-hand sweep) and encoded
twice-through with x264 to land under the size budget.
"""
import argparse
import json
import os
import subprocess
import sys
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "Tools", "demo"))
from mix import SR, load, lowpass  # noqa: E402

FPS = 60
CLIPS = os.path.join(ROOT, "Builds", "Trailer", "clips")
WORK = os.path.join(ROOT, "Builds", "Trailer")
BAR = 4 * 60 / 96  # every music loop is 96 BPM in 4/4


def run(cmd, **kw):
    print("[compose] " + " ".join(cmd[:6]) + (" ..." if len(cmd) > 6 else ""), flush=True)
    return subprocess.run(["nice", "-n", "10"] + cmd, check=True, **kw)


def frames_of(path):
    out = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-count_packets",
                          "-show_entries", "stream=nb_read_packets", "-of", "csv=p=0", path],
                         capture_output=True, text=True, check=True).stdout
    return int(out.strip())


def read_log(path):
    """The clip's audio event log: first-frame time, per-frame mixer state and one-shots."""
    t0, frames, shots = None, [], []
    for line in open(path):
        p = line.split()
        if not p:
            continue
        if p[0] == "V":
            t0 = float(p[1])
        elif p[0] == "F":
            frames.append([float(v) for v in p[1:8]])
        elif p[0] == "S":
            shots.append((float(p[1]), p[2], float(p[3]), float(p[4]), p[5] == "1"))
    return t0, np.array(frames) if frames else np.zeros((0, 7)), shots


def place(bus, x, at):
    s = int(round(at * SR))
    if s < 0:
        x, s = x[-s:], 0
    m = min(len(x), len(bus) - s)
    if m > 0:
        bus[s:s + m] += x[:m]


def resample(data, pitch):
    if abs(pitch - 1.0) < 1e-4:
        return data
    pos = np.arange(0, len(data) - 1, pitch)
    i0 = pos.astype(int)
    fr = (pos - i0)[:, None]
    return data[i0] * (1 - fr) + data[i0 + 1] * fr


def envelope(x, attack=0.008, release=0.28, block=0.005):
    """Peak-ish envelope follower (per 5 ms block, then back to samples)."""
    n = int(block * SR)
    nb = len(x) // n + 1
    pad = np.zeros(nb * n)
    pad[:len(x)] = np.abs(x).max(axis=1)
    peaks = pad.reshape(nb, n).max(axis=1)
    ka, kr = 1 - np.exp(-block / attack), 1 - np.exp(-block / release)
    env = np.zeros(nb)
    e = 0.0
    for i, v in enumerate(peaks):
        e += (v - e) * (ka if v > e else kr)
        env[i] = e
    return np.interp(np.arange(len(x)) / n, np.arange(nb), env)


def write_wav(path, x):
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype(np.int16).tobytes())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(ROOT, "docs", "media", "trailer.mp4"))
    ap.add_argument("--mb", type=float, default=36.0, help="target file size")
    ap.add_argument("--audio-only", action="store_true")
    ap.add_argument("--stems", action="store_true", help="also write the music and effects buses")
    args = ap.parse_args()

    shots = [s for s in json.load(open(os.path.join(HERE, "shots.json")))["shots"] if not s.get("stillsOnly")]

    # ---------------------------------------------------------------- timeline
    clips, t = [], 0.0
    for i, s in enumerate(shots):
        video = os.path.join(CLIPS, s["name"] + ".video.mp4")
        if not os.path.exists(video):
            sys.exit(f"missing clip {video}: run Tools/trailer/record.sh")
        n = frames_of(video)
        dur = n / FPS
        xf = 0.0 if i == 0 else max(1.0 / FPS, float(s.get("xfade", 0.4)))
        start = 0.0 if i == 0 else t - xf
        clips.append(dict(shot=s, video=video, log=video.replace(".video.mp4", ".audio.log"), frames=n,
                          dur=dur, start=start, xfade=xf, transition=s.get("transition", "fade")))
        t = start + dur
    total = t
    print(f"[compose] {len(clips)} shots, {total:.2f}s")
    for c in clips:
        print(f"  {c['start']:7.2f}  {c['dur']:5.2f}s  {c['transition'] if c['xfade'] > 0.02 else 'cut':9} {c['shot']['name']}")

    # ---------------------------------------------------------------- effects bus
    n = int((total + 3.0) * SR)
    world, ui = np.zeros((n, 2)), np.zeros((n, 2))
    muffle, focus = np.zeros(n), np.zeros(n)
    for c in clips:
        t0, F, events = read_log(c["log"])
        if t0 is None:
            continue
        a, b = int(c["start"] * SR), int((c["start"] + c["dur"]) * SR)
        if len(F):
            ts = c["start"] + (F[:, 0] - t0)
            idx = np.arange(a, min(b, n)) / SR
            muffle[a:b] = np.maximum(muffle[a:b], np.interp(idx, ts, F[:, 5]))
            focus[a:b] = np.maximum(focus[a:b], np.interp(idx, ts, F[:, 6]))
        for et, name, vol, pitch, is_world in events:
            place(world if is_world else ui, resample(load(name), pitch) * vol, c["start"] + et - t0)
        for et, name, gain in c["shot"].get("sfx", []):
            place(ui, load(name) * gain * 0.72, c["start"] + et)
    m2 = muffle[:, None]
    world = world * (1 - m2) + lowpass(world, 1400) * m2
    sfx = world + ui

    # ---------------------------------------------------------------- music bed
    sections = []
    for c in clips:
        s = c["shot"]
        if "music" in s:
            sections.append(dict(track=s["music"], start=c["start"], gain=s.get("musicGain", 1.0), lowpass=s.get("musicLowpass")))
    music = np.zeros((n, 2))
    for k, sec in enumerate(sections):
        end = sections[k + 1]["start"] if k + 1 < len(sections) else total
        a, b = int(sec["start"] * SR), min(n, int((end + 0.9) * SR))  # ring on under the next downbeat
        loop = load(sec["track"])
        reps = (b - a) // len(loop) + 1
        x = np.tile(loop, (reps, 1))[: b - a].copy()
        if sec["lowpass"]:
            x = lowpass(x, sec["lowpass"])
        env = np.ones(b - a)
        fin = int(0.03 * SR)
        env[:fin] = np.linspace(0, 1, fin)
        tail = (b - a) - int((end - sec["start"]) * SR)
        if tail > 0:
            env[-tail:] *= np.linspace(1, 0, tail) ** 2
        music[a:b] += x * (env * sec["gain"])[:, None]
    # muffled while the player is frozen, lighter while focusing (as AudioDirector does in game)
    mm = (muffle * 0.8)[:, None]
    music = music * (1 - mm) + lowpass(music, 650) * mm
    fm = (focus * 0.7)[:, None]
    music = music * (1 - fm) + lowpass(music, 2400) * fm
    # duck under the effects: up to 7 dB, by how far the effects peak above -24 dBFS
    env = envelope(sfx)
    over = np.clip(20 * np.log10(np.maximum(env, 1e-6)) + 24, 0, None)
    music *= (10 ** (-np.minimum(over * 0.45, 7.0) / 20))[:, None]

    mix = music * 0.62 + sfx
    if args.stems:
        for name, bus in (("music", music * 0.62), ("sfx", sfx)):
            write_wav(os.path.join(WORK, f"stem_{name}.wav"), bus[: int(total * SR)])
    tt = np.arange(n) / SR
    fade = np.clip(tt / 0.25, 0, 1) * np.clip((total - tt) / 2.2, 0, 1) ** 1.5
    mix *= fade[:, None]
    mix = mix[: int(total * SR)]
    peak = np.abs(mix).max()
    mix *= 0.89 / max(peak, 1e-6)
    raw = os.path.join(WORK, "trailer_mix.wav")
    write_wav(raw, mix)
    print(f"[compose] mix written, peak before trim {peak:.2f}")

    # two-pass loudness normalisation to -16 LUFS, true peak -1.5 dB
    meas = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", raw, "-af",
                           "loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json", "-f", "null", "-"],
                          capture_output=True, text=True).stderr
    j = json.loads(meas[meas.rindex("{"):meas.rindex("}") + 1])
    audio = os.path.join(WORK, "trailer_audio.wav")
    run(["ffmpeg", "-y", "-v", "error", "-i", raw, "-af",
         f"loudnorm=I=-16:TP=-1.5:LRA=11:measured_I={j['input_i']}:measured_TP={j['input_tp']}:"
         f"measured_LRA={j['input_lra']}:measured_thresh={j['input_thresh']}:offset={j['target_offset']}:linear=true,"
         f"aresample=48000", audio])
    if args.audio_only:
        return

    # ---------------------------------------------------------------- pictures
    inputs, parts = [], []
    for i, c in enumerate(clips):
        inputs += ["-i", c["video"]]
        parts.append(f"[{i}:v]settb=AVTB,fps={FPS},format=yuv420p,setpts=PTS-STARTPTS[v{i}]")
    last = "v0"
    for i, c in enumerate(clips[1:], 1):
        tr = c["transition"] if c["xfade"] > 0.02 else "fade"
        parts.append(f"[{last}][v{i}]xfade=transition={tr}:duration={c['xfade']:.4f}:offset={c['start']:.4f}[x{i}]")
        last = f"x{i}"
    parts.append(f"[{last}]fade=t=in:st=0:d=0.25,fade=t=out:st={total - 1.2:.3f}:d=1.2,format=yuv420p[vout]")
    graph = os.path.join(WORK, "trailer.filtergraph")
    open(graph, "w").write(";\n".join(parts))

    kbps = int(args.mb * 8e6 / total / 1000) - 192
    x264 = ["-c:v", "libx264", "-preset", "slow", "-b:v", f"{kbps}k", "-maxrate", f"{int(kbps * 2.2)}k",
            "-bufsize", f"{kbps * 4}k", "-profile:v", "high", "-pix_fmt", "yuv420p",
            "-x264-params", "aq-mode=3:aq-strength=0.85:deblock=-1,-1", "-g", "120"]
    log = os.path.join(WORK, "x264pass")
    common = ["ffmpeg", "-y", "-v", "error", "-stats"] + inputs + ["-/filter_complex", graph, "-map", "[vout]"]
    run(common + x264 + ["-pass", "1", "-passlogfile", log, "-an", "-f", "mp4", os.devnull])
    video = os.path.join(WORK, "trailer_video.mp4")
    run(common + x264 + ["-pass", "2", "-passlogfile", log, "-an", video])
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    run(["ffmpeg", "-y", "-v", "error", "-i", video, "-i", audio, "-map", "0:v", "-map", "1:a",
         "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", args.out])
    print(f"[compose] {args.out}: {os.path.getsize(args.out) / 1e6:.1f} MB, {total:.1f}s, video {kbps} kb/s")
    json.dump([dict(name=c["shot"]["name"], start=round(c["start"], 3), dur=round(c["dur"], 3)) for c in clips],
              open(os.path.join(WORK, "timeline.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
