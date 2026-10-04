"""Rebuilds the demo soundtrack from the game's audio event log (see AudioDirector.Log).

    mix.py EVENTS.audio.log OUT.wav

Log lines (times are game seconds, frame-locked to the captured video):
    V t0 fps                                first captured frame
    F t vol0 pitch0 vol1 pitch1 muffle focus  music decks + filters, once per frame
    M t deck clip                           a music deck (re)starts a loop from the top
    S t clip volume pitch world             a one-shot (world sounds get the frozen muffle)
The mixer mirrors AudioDirector: a 1.4 kHz low-pass on world one-shots and 600 Hz on music,
blended by the muffle amount; deck volume and pitch follow the per-frame values.
"""
import os
import sys
import wave

import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
AUDIO = os.path.join(ROOT, "Assets", "Resources", "Audio")
_cache = {}


def load(name):
    if name not in _cache:
        with wave.open(os.path.join(AUDIO, name + ".wav")) as w:
            assert w.getframerate() == SR, name
            a = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768
            a = a.reshape(-1, w.getnchannels())
        _cache[name] = a if a.shape[1] == 2 else np.repeat(a, 2, axis=1)
    return _cache[name]


def lowpass(x, fc):
    """Two-pole (Butterworth-magnitude) low-pass, applied in the frequency domain."""
    n = len(x)
    size = 1 << (n - 1).bit_length()
    X = np.fft.rfft(x, size, axis=0)
    f = np.fft.rfftfreq(size, 1 / SR)
    g = 1 / np.sqrt(1 + (f / fc) ** 4)
    return np.fft.irfft(X * g[:, None], size, axis=0)[:n]


def main(log_path, out_path):
    t0, frames, music, shots = None, [], [], []
    for line in open(log_path):
        p = line.split()
        if not p:
            continue
        if p[0] == "V":
            t0 = float(p[1])
        elif p[0] == "F":
            frames.append([float(v) for v in p[1:8]])
        elif p[0] == "M":
            music.append((float(p[1]), int(p[2]), p[3]))
        elif p[0] == "S":
            shots.append((float(p[1]), p[2], float(p[3]), float(p[4]), p[5] == "1"))
    if t0 is None or not frames:
        sys.exit("no frames in log")
    F = np.array(frames)
    n = int(round((F[-1, 0] - t0 + 1 / 60) * SR))
    at = lambda t: int(round((t - t0) * SR))
    ft = (F[:, 0] - t0) * SR
    idx = np.arange(n)
    curve = lambda col: np.interp(idx, ft, F[:, col])
    muffle = curve(5)[:, None]

    # music decks
    mus = np.zeros((n, 2))
    for deck, (vcol, pcol) in enumerate([(1, 2), (3, 4)]):
        vol, pitch = curve(vcol), curve(pcol)
        starts = sorted((at(t), clip) for t, d, clip in music if d == deck)
        for k, (s0, clip) in enumerate(starts):
            s1 = starts[k + 1][0] if k + 1 < len(starts) else n
            a, b = max(0, s0), min(n, s1)
            if b <= a:
                continue
            data = load(clip)
            pos = np.cumsum(pitch[a:b]) - pitch[a] + (a - s0)
            pos %= len(data)
            i0 = np.floor(pos).astype(int)
            fr = (pos - i0)[:, None]
            i1 = (i0 + 1) % len(data)
            mus[a:b] += (data[i0] * (1 - fr) + data[i1] * fr) * vol[a:b, None]
    mus = mus * (1 - muffle) + lowpass(mus, 600) * muffle

    # one-shots
    world = np.zeros((n, 2))
    ui = np.zeros((n, 2))
    for t, clip, vol, pitch, is_world in shots:
        data = load(clip)
        pos = np.arange(0, len(data) - 1, pitch)
        i0 = pos.astype(int)
        fr = (pos - i0)[:, None]
        x = (data[i0] * (1 - fr) + data[i0 + 1] * fr) * vol
        s = at(t)
        if s < 0:
            x, s = x[-s:], 0
        m = min(len(x), n - s)
        if m > 0:
            (world if is_world else ui)[s:s + m] += x[:m]
    world = world * (1 - muffle) + lowpass(world, 1400) * muffle

    out = mus + world + ui
    a = np.abs(out)
    knee = 0.9
    out = np.where(a > knee, np.sign(out) * (knee + (1 - knee) * np.tanh((a - knee) / (1 - knee))), out)
    print(f"[mix] {n / SR:.2f}s, {len(shots)} one-shots, {len(music)} music starts, peak {a.max():.2f}")
    with wave.open(out_path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(out, -1, 1) * 32767).astype(np.int16).tobytes())


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
