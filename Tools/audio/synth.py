#!/usr/bin/env python3
"""
Borrowed Seconds: procedural sound effects and music (numpy only, no samples).

    Tools/audio/synth.sh                 # everything
    Tools/audio/synth.sh sfx             # effects only
    Tools/audio/synth.sh music_a         # one item

Writes 16-bit WAVs to Assets/Resources/Audio/. Music loops are 96 BPM, 16 bars, and wrap
their reverb/release tails around so they loop seamlessly.
"""
import math
import os
import sys
import wave

import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets", "Resources", "Audio")
rng = np.random.default_rng(7)


# ------------------------------------------------------------------ primitives

def t_axis(dur):
    return np.arange(int(dur * SR)) / SR


def env(n, a=0.005, d=0.1, s=0.0, r=0.05, hold=0.0):
    """ADSR over n samples (seconds for a/d/r/hold)."""
    out = np.zeros(n)
    ia, idd, ih, ir = int(a * SR), int(d * SR), int(hold * SR), int(r * SR)
    i = 0
    seg = min(ia, n - i)
    if seg > 0:
        out[i:i + seg] = np.linspace(0, 1, seg, endpoint=False)
    i += seg
    seg = min(idd, n - i)
    if seg > 0:
        out[i:i + seg] = np.linspace(1, s, seg, endpoint=False)
    i += seg
    seg = min(ih, n - i)
    if seg > 0:
        out[i:i + seg] = s
    i += seg
    seg = min(ir, n - i)
    if seg > 0:
        out[i:i + seg] = np.linspace(s, 0, seg)
    return out


def expdecay(n, tau):
    return np.exp(-np.arange(n) / (tau * SR))


def osc(freq, n, shape="sine", phase=0.0):
    """freq: scalar or per-sample array."""
    f = np.broadcast_to(np.asarray(freq, dtype=float), (n,))
    ph = 2 * np.pi * np.cumsum(f) / SR + phase
    if shape == "sine":
        return np.sin(ph)
    if shape == "tri":
        return 2 / np.pi * np.arcsin(np.sin(ph))
    if shape == "saw":
        # additive, band-limited enough for our ranges
        out = np.zeros(n)
        f0 = float(np.max(f))
        for k in range(1, max(2, int(9000 / max(f0, 1)))):
            out += np.sin(ph * k) / k * (-1) ** (k + 1)
        return out * (2 / np.pi)
    if shape == "square":
        out = np.zeros(n)
        f0 = float(np.max(f))
        for k in range(1, max(2, int(9000 / max(f0, 1))), 2):
            out += np.sin(ph * k) / k
        return out * (4 / np.pi)
    raise ValueError(shape)


def noise(n):
    return rng.standard_normal(n)


def fft_filter(x, lo=None, hi=None, q=1.0, resonance=0.0, center=None):
    """Smooth spectral band filter: highpass at lo, lowpass at hi (Hz). Optional resonant peak."""
    n = len(x)
    if n == 0:
        return x
    size = 1 << (n - 1).bit_length()
    X = np.fft.rfft(x, size)
    f = np.fft.rfftfreq(size, 1 / SR)
    g = np.ones_like(f)
    if lo:
        g *= 1 / (1 + (lo / np.maximum(f, 1e-3)) ** (2 * q))
    if hi:
        g *= 1 / (1 + (f / hi) ** (2 * q))
    if center and resonance:
        g *= 1 + resonance * np.exp(-((f - center) / (center * 0.08)) ** 2)
    return np.fft.irfft(X * g, size)[:n]


def sweep_filter(x, f0, f1, width=0.5, steps=24):
    """Band-pass whose centre glides from f0 to f1 (block-wise crossfade)."""
    n = len(x)
    out = np.zeros(n)
    edges = np.linspace(0, n, steps + 1).astype(int)
    for i in range(steps):
        c = f0 * (f1 / f0) ** (i / max(1, steps - 1))
        a, b = max(0, edges[i] - 512), min(n, edges[i + 1] + 512)
        seg = fft_filter(x[a:b], lo=c * (1 - width), hi=c * (1 + width), q=2)
        w = np.ones(b - a)
        fade = min(512, (b - a) // 2)
        if a > 0:
            w[:fade] = np.linspace(0, 1, fade)
        if b < n:
            w[-fade:] = np.linspace(1, 0, fade)
        out[a:b] += seg * w
    return out


def reverb(x, seconds=1.6, mix=0.25, stereo=False, damp=4000, predelay=0.012):
    n = len(x)
    ir_n = int(seconds * SR)
    chans = []
    for c in range(2 if stereo else 1):
        ir = noise(ir_n) * np.exp(-np.arange(ir_n) / (seconds * SR / 6.9))
        ir = fft_filter(ir, lo=180, hi=damp)
        ir = np.concatenate([np.zeros(int(predelay * SR)), ir])
        ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
        size = 1 << (n + len(ir)).bit_length()
        wet = np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[:n + len(ir)]
        dry = np.concatenate([x, np.zeros(len(ir))])
        chans.append(dry * (1 - mix) + wet * mix * 0.6)
    return np.stack(chans, axis=1) if stereo else chans[0]


def fit(x, n):
    if len(x) >= n:
        return x[:n]
    return np.concatenate([x, np.zeros(n - len(x))])


def mix_at(buf, x, at):
    i = int(at * SR)
    if i >= len(buf):
        return
    m = min(len(x), len(buf) - i)
    buf[i:i + m] += x[:m]


def norm(x, peak=0.89):
    m = np.max(np.abs(x)) + 1e-9
    return x / m * peak


def softclip(x, drive=1.0):
    return np.tanh(x * drive) / np.tanh(drive)


def write(name, x, peak=0.89, fade_out=0.004):
    os.makedirs(OUT, exist_ok=True)
    x = np.asarray(x, dtype=float)
    if fade_out and x.ndim == 1:
        # drop the inaudible end of reverb tails
        loud = np.nonzero(np.abs(x) > np.max(np.abs(x)) * 0.003)[0]
        if len(loud):
            x = x[:min(len(x), loud[-1] + int(0.02 * SR))]
        k = min(len(x), int(fade_out * SR))
        x[-k:] *= np.linspace(1, 0, k)
    x = norm(x, peak)
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    ch = 1 if data.ndim == 1 else data.shape[1]
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print(f"[audio] {name:16s} {len(data) / SR:6.2f}s peak {peak:.2f}")


def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


# ------------------------------------------------------------------ instruments

def bell(freq, dur, bright=1.0, decay=0.9):
    n = int(dur * SR)
    out = np.zeros(n)
    for ratio, amp, tau in [(1, 1, decay), (2.76, 0.45 * bright, decay * 0.45), (5.4, 0.22 * bright, decay * 0.25), (8.93, 0.1 * bright, decay * 0.12)]:
        out += amp * osc(freq * ratio, n) * expdecay(n, tau)
    out *= env(n, a=0.002, d=0.0, s=1.0, r=0.0, hold=dur)
    return out


def kalimba(freq, dur=1.2):
    n = int(dur * SR)
    out = osc(freq, n) * expdecay(n, 0.55)
    out += 0.35 * osc(freq * 5.95, n) * expdecay(n, 0.06)
    out += 0.12 * osc(freq * 3.0, n) * expdecay(n, 0.12)
    click = fft_filter(noise(int(0.006 * SR)), lo=2000, hi=8000) * 0.15
    out[:len(click)] += click
    return out * env(n, a=0.002, d=0, s=1, r=0.02, hold=dur)


def marimba(freq, dur=0.9):
    n = int(dur * SR)
    out = osc(freq, n) * expdecay(n, 0.32)
    out += 0.28 * osc(freq * 4.0, n) * expdecay(n, 0.05)
    out += 0.1 * osc(freq * 9.2, n) * expdecay(n, 0.015)
    return out * env(n, a=0.003, d=0, s=1, r=0.02, hold=dur)


def pad(freqs, dur, bright=1800, attack=1.2, release=1.4):
    n = int(dur * SR)
    out = np.zeros(n)
    for f in freqs:
        for det in (-0.08, 0.0, 0.07):
            fr = f * 2 ** (det / 12)
            lfo = 1 + 0.002 * np.sin(2 * np.pi * (0.13 + rng.random() * 0.2) * np.arange(n) / SR + rng.random() * 6)
            out += osc(fr * lfo, n, "saw", rng.random() * 6) * 0.33
    out = fft_filter(out, lo=90, hi=bright, q=1.5)
    return out * env(n, a=attack, d=0, s=1, r=release, hold=max(0, dur - attack - release)) / max(1, len(freqs))


def sub(freq, dur):
    n = int(dur * SR)
    return (osc(freq, n) + 0.15 * osc(freq * 2, n)) * env(n, a=0.02, d=0.2, s=0.7, r=0.15, hold=max(0, dur - 0.4))


def kick(dur=0.35):
    n = int(dur * SR)
    f = 48 + 90 * np.exp(-np.arange(n) / (0.03 * SR))
    return osc(f, n) * expdecay(n, 0.12) * env(n, a=0.001, d=0, s=1, r=0.02, hold=dur)


def tick_click(freq=3200, dur=0.04, tone=0.6):
    n = int(dur * SR)
    out = fft_filter(noise(n), lo=freq * 0.6, hi=freq * 1.6, q=2) * expdecay(n, 0.006)
    out += tone * osc(freq, n) * expdecay(n, 0.008)
    return out


def shaker(dur=0.08):
    n = int(dur * SR)
    return fft_filter(noise(n), lo=6000, hi=14000) * env(n, a=0.01, d=0.06, s=0, r=0.01)


# ------------------------------------------------------------------ sound effects

def sfx_borrow():
    d = 1.1
    n = int(d * SR)
    buf = np.zeros(n)
    f = 380 * (1700 / 380) ** np.clip(np.arange(n) / (0.35 * SR), 0, 1)
    shimmer = sum(osc(f * r * 2 ** (det / 12), n) * a for r, a, det in [(1, 1, 0), (1.5, 0.5, 0.1), (2, 0.4, -0.1), (3, 0.2, 0.05)])
    buf += shimmer * env(n, a=0.25, d=0.6, s=0, r=0.05) * 0.35
    crack_n = int(0.12 * SR)
    crack = fft_filter(noise(crack_n), lo=2500, hi=12000) * expdecay(crack_n, 0.02)
    for k in range(5):
        mix_at(buf, crack * (0.7 - k * 0.1), 0.28 + k * 0.018)
    for k in range(10):
        mix_at(buf, bell(2000 + rng.random() * 3500, 0.4, 0.6, 0.12) * 0.12, 0.3 + rng.random() * 0.25)
    thump_n = int(0.35 * SR)
    thump = osc(40 + 50 * np.exp(-np.arange(thump_n) / (0.05 * SR)), thump_n) * expdecay(thump_n, 0.1)
    mix_at(buf, thump * 0.9, 0.28)
    return reverb(buf, 1.4, 0.3)


def sfx_freeze():
    d = 1.2
    n = int(d * SR)
    buf = np.zeros(n)
    f = 1800 * (420 / 1800) ** np.clip(np.arange(n) / (0.5 * SR), 0, 1)
    buf += sum(osc(f * r, n) * a for r, a in [(1, 1), (1.5, 0.4), (2.01, 0.3)]) * env(n, a=0.01, d=0.7, s=0, r=0.1) * 0.3
    for k in range(40):
        at = rng.random() ** 1.5 * 0.7
        mix_at(buf, tick_click(3000 + rng.random() * 6000, 0.03, 0.2) * (0.25 + rng.random() * 0.3), at)
    whoomp_n = int(0.5 * SR)
    whoomp = fft_filter(noise(whoomp_n), hi=300) * env(whoomp_n, a=0.05, d=0.4, s=0, r=0.05)
    buf[:whoomp_n] += whoomp * 1.2
    low = osc(110, n) * env(n, a=0.02, d=0.8, s=0, r=0.1) * 0.25
    return reverb(buf + low, 1.8, 0.35)


def sfx_thaw(scale=1.0):
    d = 0.9
    n = int(d * SR)
    buf = np.zeros(n)
    burst_n = int(0.25 * SR)
    burst = fft_filter(noise(burst_n), lo=1500, hi=13000) * expdecay(burst_n, 0.05)
    buf[:burst_n] += burst * 0.8
    for k in range(int(26 * scale)):
        at = rng.random() ** 2 * 0.35
        mix_at(buf, bell(2200 + rng.random() * 5000, 0.3, 0.4, 0.06 + rng.random() * 0.06) * (0.2 + rng.random() * 0.25), at)
    whoosh = fft_filter(noise(n), lo=400, hi=3000) * env(n, a=0.005, d=0.5, s=0, r=0.05) * 0.3
    return reverb(buf + whoosh, 1.2, 0.3)


def sfx_step(i):
    d = 0.11
    n = int(d * SR)
    f = 1150 + i * 140
    tap = osc(f, n) * expdecay(n, 0.012) * 0.6 + osc(f * 2.3, n) * expdecay(n, 0.006) * 0.25
    click = fft_filter(noise(n), lo=1500, hi=7000) * expdecay(n, 0.004) * 0.5
    thud = osc(180 + i * 10, n) * expdecay(n, 0.02) * 0.5
    return tap + click + thud


def sfx_bump():
    n = int(0.16 * SR)
    return osc(110 * np.exp(-np.arange(n) / (0.2 * SR)), n) * expdecay(n, 0.035) + fft_filter(noise(n), hi=900) * expdecay(n, 0.02) * 0.6


def sfx_slider_thunk():
    n = int(0.25 * SR)
    body = osc(85 * (1 + 0.5 * np.exp(-np.arange(n) / (0.01 * SR))), n) * expdecay(n, 0.06)
    wood = fft_filter(noise(n), lo=200, hi=1600) * expdecay(n, 0.025) * 0.7
    ring = osc(420, n) * expdecay(n, 0.04) * 0.15
    return body + wood + ring


def sfx_rotor_whoosh():
    n = int(0.32 * SR)
    x = sweep_filter(noise(n), 500, 1400, 0.45, 12) * env(n, a=0.1, d=0.2, s=0, r=0.02)
    return x


def sfx_rotor_clank():
    n = int(0.45 * SR)
    out = sum(osc(f, n) * a * expdecay(n, tau) for f, a, tau in [(523, 1, 0.12), (1307, 0.6, 0.07), (2177, 0.4, 0.05), (3460, 0.2, 0.03)])
    out += fft_filter(noise(n), lo=1000, hi=6000) * expdecay(n, 0.01) * 0.8
    return reverb(out, 0.8, 0.2)


def sfx_laser_zap():
    n = int(0.22 * SR)
    f = 2200 * (520 / 2200) ** np.clip(np.arange(n) / (0.12 * SR), 0, 1)
    x = osc(f, n, "saw") * 0.5 + osc(f * 0.5, n, "square") * 0.2
    x = fft_filter(x, lo=300, hi=6000) * env(n, a=0.002, d=0.18, s=0, r=0.02)
    buzz = osc(120, n, "saw") * 0.15 * env(n, a=0.01, d=0.2, s=0, r=0.01)
    return x + buzz


def sfx_plate():
    n = int(0.5 * SR)
    click = tick_click(2000, 0.05, 0.3)
    chime = (osc(880, n) * 0.6 + osc(1320, n) * 0.4) * expdecay(n, 0.15)
    out = chime * 0.5
    out[:len(click)] += click
    return reverb(out, 0.9, 0.2)


def sfx_gate():
    n = int(0.42 * SR)
    slide = sweep_filter(noise(n), 900, 400, 0.5, 10) * env(n, a=0.04, d=0.3, s=0, r=0.05)
    hum = osc(95, n) * env(n, a=0.05, d=0.3, s=0, r=0.05) * 0.4
    end = tick_click(700, 0.06, 0.5)
    out = slide + hum
    mix_at(out, end * 0.6, 0.33)
    return out


def sfx_lock_tick():
    return reverb(bell(659.25, 0.5, 0.8, 0.25), 1.0, 0.25)


def sfx_lock_reset():
    n = int(0.25 * SR)
    f = 600 * (240 / 600) ** (np.arange(n) / n)
    return osc(f, n, "tri") * env(n, a=0.005, d=0.22, s=0, r=0.02)


def sfx_lock_latch():
    d = 2.2
    n = int(d * SR)
    buf = np.zeros(n)
    for k, m in enumerate([60, 64, 67, 72, 76]):
        mix_at(buf, bell(midi(m + 7), 1.8, 0.7, 0.9) * 0.35, k * 0.035)
    clunk_n = int(0.3 * SR)
    clunk = osc(70 + 60 * np.exp(-np.arange(clunk_n) / (0.02 * SR)), clunk_n) * expdecay(clunk_n, 0.08)
    buf[:clunk_n] += clunk
    buf += osc(midi(43), n) * env(n, a=0.01, d=1.5, s=0, r=0.1) * 0.25
    return reverb(buf, 2.4, 0.35)


def sfx_exit_open():
    d = 1.8
    n = int(d * SR)
    buf = np.zeros(n)
    for k, m in enumerate([72, 76, 79, 83, 86, 88, 91]):
        mix_at(buf, bell(midi(m), 0.9, 0.5, 0.4) * 0.3, k * 0.07)
    buf += fft_filter(noise(n), lo=5000, hi=14000) * env(n, a=0.3, d=0.9, s=0, r=0.1) * 0.08
    return reverb(buf, 2.0, 0.4)


def sfx_win():
    d = 2.6
    n = int(d * SR)
    buf = np.zeros(n)
    for k, m in enumerate([60, 64, 67, 72, 76, 79, 84]):
        mix_at(buf, marimba(midi(m), 1.0) * 0.45, k * 0.085)
    mix_at(buf, pad([midi(60), midi(64), midi(67), midi(71)], 2.0, 2500, 0.3, 1.2) * 0.5, 0.5)
    buf += fft_filter(noise(n), lo=6000, hi=15000) * env(n, a=0.6, d=1.4, s=0, r=0.1) * 0.05
    return reverb(buf, 2.2, 0.35)


def sfx_death():
    d = 1.3
    n = int(d * SR)
    buf = np.zeros(n)
    burst_n = int(0.4 * SR)
    buf[:burst_n] += fft_filter(noise(burst_n), lo=800, hi=12000) * expdecay(burst_n, 0.08)
    for k in range(30):
        mix_at(buf, bell(1500 + rng.random() * 5000, 0.4, 0.5, 0.08) * 0.2, rng.random() ** 2 * 0.4)
    boom_n = int(0.9 * SR)
    boom = osc(55 * np.exp(-np.arange(boom_n) / (0.6 * SR)) + 20, boom_n) * expdecay(boom_n, 0.3)
    buf[:boom_n] += boom * 1.1
    dis = osc(midi(49), n, "saw") * 0.12 + osc(midi(50), n, "saw") * 0.12
    buf += fft_filter(dis, hi=1500) * env(n, a=0.01, d=0.9, s=0, r=0.1)
    return reverb(buf, 1.8, 0.35)


def sfx_rewind():
    d = 0.9
    n = int(d * SR)
    x = sweep_filter(noise(n), 3500, 400, 0.5, 18) * env(n, a=0.05, d=0.75, s=0, r=0.05)
    f = 900 * (220 / 900) ** (np.arange(n) / n) * (1 + 0.03 * np.sin(2 * np.pi * 9 * np.arange(n) / SR))
    tone = osc(f, n, "tri") * env(n, a=0.05, d=0.8, s=0, r=0.05) * 0.4
    rev = fft_filter(noise(n), lo=4000, hi=12000) * np.linspace(0, 1, n) ** 3 * 0.3
    return x + tone + rev


def sfx_denied():
    n = int(0.24 * SR)
    a = osc(196, n // 2, "square") * env(n // 2, a=0.005, d=0.1, s=0, r=0.01)
    b = osc(156, n - n // 2, "square") * env(n - n // 2, a=0.005, d=0.1, s=0, r=0.01)
    return fft_filter(np.concatenate([a, b]), hi=1800) * 0.6


def sfx_tick():
    return tick_click(2600, 0.06, 0.8)


def sfx_ui(kind):
    if kind == "hover":
        return tick_click(4200, 0.035, 0.5) * 0.6
    if kind == "tick":
        return tick_click(3000, 0.03, 0.6) * 0.5
    if kind == "click":
        n = int(0.3 * SR)
        out = (osc(1046.5, n) * 0.5 + osc(1568, n) * 0.3) * expdecay(n, 0.08)
        out[:int(0.04 * SR)] += tick_click(2400, 0.04, 0.5)
        return reverb(out, 0.6, 0.15)
    if kind == "back":
        n = int(0.25 * SR)
        out = osc(523.25, n) * expdecay(n, 0.07) * 0.6
        out[:int(0.04 * SR)] += tick_click(1400, 0.04, 0.5)
        return reverb(out, 0.6, 0.15)
    raise ValueError(kind)


def sfx_stamp():
    n = int(0.5 * SR)
    thump = osc(70 + 80 * np.exp(-np.arange(n) / (0.015 * SR)), n) * expdecay(n, 0.07)
    slap = fft_filter(noise(n), lo=600, hi=5000) * expdecay(n, 0.02) * 0.8
    ring = bell(1318.5, 0.5, 0.6, 0.2) * 0.25
    return reverb(thump + slap + fit(ring, n), 0.8, 0.2)


def sfx_chapter():
    d = 3.6
    n = int(d * SR)
    buf = np.zeros(n)
    gong = sum(osc(f, n) * a * expdecay(n, tau) for f, a, tau in [(98, 1, 1.6), (196.7, 0.5, 1.2), (263, 0.3, 0.8), (391, 0.2, 0.6), (523, 0.12, 0.4)])
    buf += gong * env(n, a=0.02, d=0, s=1, r=0.3, hold=d)
    buf += pad([midi(50), midi(57), midi(62), midi(69)], d, 1600, 1.0, 1.8) * 0.6
    for k, m in enumerate([74, 81, 86]):
        mix_at(buf, bell(midi(m), 1.6, 0.4, 0.8) * 0.15, 0.4 + k * 0.4)
    return reverb(buf, 3.0, 0.4)


# ------------------------------------------------------------------ music

BPM = 96
BEAT = 60 / BPM
BAR = BEAT * 4


def chord_tones(root, kind):
    shapes = {"maj7": [0, 4, 7, 11], "m7": [0, 3, 7, 10], "m9": [0, 3, 7, 10, 14], "maj9": [0, 4, 7, 11, 14],
              "6": [0, 4, 7, 9], "sus": [0, 5, 7, 10], "add9": [0, 4, 7, 14], "m": [0, 3, 7], "maj": [0, 4, 7]}
    return [root + i for i in shapes[kind]]


def render_track(name, chords, bars_per_chord, layers, bars=16, stereo_width=0.35):
    """chords: list of (root midi, kind). layers: set of layer names."""
    length = bars * BAR
    tail = 4.0
    total = int((length + tail) * SR)
    L = np.zeros(total)
    R = np.zeros(total)
    seq_rng = np.random.default_rng(sum(ord(c) * 31 ** i for i, c in enumerate(name)) % (2 ** 32))

    def put(x, at, pan=0.0, gain=1.0):
        i = int(at * SR)
        m = min(len(x), total - i)
        if m <= 0:
            return
        L[i:i + m] += x[:m] * gain * (1 - pan) * 0.7071 * 1.2
        R[i:i + m] += x[:m] * gain * (1 + pan) * 0.7071 * 1.2

    n_chords = bars // bars_per_chord
    for ci in range(n_chords):
        root, kind = chords[ci % len(chords)]
        tones = chord_tones(root, kind)
        t0 = ci * bars_per_chord * BAR
        dur = bars_per_chord * BAR
        if "pad" in layers:
            p = pad([midi(m) for m in tones], dur + 1.2, 1500 if "dark" in layers else 2200, 0.8, 1.2)
            put(p, t0, -0.3, 0.55)
            put(pad([midi(m + 12) for m in tones[:3]], dur + 1.2, 2800, 1.2, 1.2), t0, 0.3, 0.18)
        if "sub" in layers:
            for b in range(bars_per_chord * (2 if "drive" in layers else 1)):
                step = BAR if "drive" not in layers else BAR / 2
                put(sub(midi(root - 24 if root >= 48 else root - 12), step * 0.95), t0 + b * step, 0, 0.55)
        # arpeggio
        arp_notes = sorted(set([m + 12 for m in tones] + [m + 24 for m in tones[:3]]))
        if "kalimba" in layers:
            pattern = [0, 2, 1, 3, 2, 4, 3, 1]
            for s in range(bars_per_chord * 8):
                m = arp_notes[pattern[s % len(pattern)] % len(arp_notes)]
                if seq_rng.random() < 0.12:
                    continue
                put(kalimba(midi(m), 1.4), t0 + s * BEAT / 2, (0.5 if s % 2 else -0.5) * stereo_width, 0.28)
        if "marimba" in layers:
            pattern = [0, 1, 2, 3, 2, 1, 4, 2, 0, 2, 3, 4, 3, 2, 1, 2]
            for s in range(bars_per_chord * 16):
                if s % 16 in (5, 13) and seq_rng.random() < 0.6:
                    continue
                m = arp_notes[pattern[s % 16] % len(arp_notes)]
                acc = 1.0 if s % 4 == 0 else 0.7
                put(marimba(midi(m), 0.6), t0 + s * BEAT / 4, (0.4 if (s // 2) % 2 else -0.4) * stereo_width, 0.22 * acc)
        if "bells" in layers:
            # a slow pentatonic melody over the chord
            scale = [0, 2, 4, 7, 9]
            base = root + 24
            for s in range(bars_per_chord * 2):
                if seq_rng.random() < 0.3:
                    continue
                deg = seq_rng.integers(0, 5)
                m = base + scale[deg] + (12 if seq_rng.random() < 0.2 else 0)
                put(bell(midi(m), 2.0, 0.5, 0.9), t0 + s * BAR / 2 + (BEAT if seq_rng.random() < 0.3 else 0), seq_rng.uniform(-0.5, 0.5), 0.12)

    beats = int(length / BEAT)
    for b in range(beats):
        t = b * BEAT
        if "ticks" in layers:
            put(tick_click(3400 if b % 2 == 0 else 2600, 0.05, 0.7), t, 0.25 if b % 2 else -0.25, 0.16)
            if "drive" in layers:
                put(tick_click(5200, 0.03, 0.3), t + BEAT / 2, 0.4, 0.06)
        if "shaker" in layers:
            for k in range(4):
                put(shaker(0.07), t + k * BEAT / 4, 0.5, 0.05 * (1.3 if k == 2 else 0.8))
        if "kick" in layers and (b % 4 in (0, 2) or ("drive" in layers and b % 4 == 3 and (b // 4) % 2 == 1)):
            put(kick(0.4), t, 0, 0.55)
        if "drive" in layers and b % 4 == 2:
            put(fft_filter(noise(int(0.18 * SR)), lo=1200, hi=9000) * expdecay(int(0.18 * SR), 0.05), t, -0.1, 0.12)

    mixdown = np.stack([L, R], axis=1)
    wet = np.stack([reverb(L, 2.8, 1.0, False, 3500)[:total], reverb(R, 2.9, 1.0, False, 3500)[:total]], axis=1)
    out = mixdown * 0.8 + wet * 0.38
    # wrap the tail into the head so the loop is seamless
    n_loop = int(length * SR)
    looped = out[:n_loop].copy()
    looped[:total - n_loop] += out[n_loop:]
    looped = softclip(looped * 1.1, 1.2)
    write(name, looped, 0.8, fade_out=0)


def music_title():
    render_track("music_title", [(62, "maj9"), (59, "m9"), (55, "maj7"), (57, "sus")], 4, {"pad", "kalimba", "ticks", "sub", "bells"})


def music_a():
    render_track("music_a", [(57, "m9"), (53, "maj7"), (48, "add9"), (55, "6")], 2, {"pad", "marimba", "ticks", "sub", "kick"})


def music_b():
    render_track("music_b", [(52, "m9"), (48, "maj7"), (57, "m7"), (59, "sus")], 2, {"pad", "marimba", "kalimba", "ticks", "shaker", "sub", "kick", "dark"})


def music_finale():
    render_track("music_finale", [(50, "m9"), (46, "maj7"), (53, "add9"), (48, "6")], 2, {"pad", "marimba", "bells", "ticks", "shaker", "sub", "kick", "drive"})


# ------------------------------------------------------------------ catalogue

SFX = {
    "borrow": (sfx_borrow, 0.9), "freeze": (sfx_freeze, 0.85), "thaw": (sfx_thaw, 0.8),
    "obstacle_thaw": (lambda: sfx_thaw(0.5), 0.6), "bump": (sfx_bump, 0.6),
    "slider_thunk": (sfx_slider_thunk, 0.7), "rotor_whoosh": (sfx_rotor_whoosh, 0.6), "rotor_clank": (sfx_rotor_clank, 0.6),
    "laser_zap": (sfx_laser_zap, 0.55), "plate": (sfx_plate, 0.7), "gate": (sfx_gate, 0.7),
    "lock_tick": (sfx_lock_tick, 0.6), "lock_reset": (sfx_lock_reset, 0.5), "lock_latch": (sfx_lock_latch, 0.9),
    "exit_open": (sfx_exit_open, 0.75), "win": (sfx_win, 0.85), "death": (sfx_death, 0.9),
    "rewind": (sfx_rewind, 0.7), "denied": (sfx_denied, 0.5), "tick": (sfx_tick, 0.6),
    "ui_hover": (lambda: sfx_ui("hover"), 0.45), "ui_tick": (lambda: sfx_ui("tick"), 0.45),
    "ui_click": (lambda: sfx_ui("click"), 0.6), "ui_back": (lambda: sfx_ui("back"), 0.55),
    "stamp": (sfx_stamp, 0.85), "chapter": (sfx_chapter, 0.8),
}
for _i in range(4):
    SFX[f"step_{_i + 1}"] = ((lambda i=_i: sfx_step(i)), 0.5)
MUSIC = {"music_title": music_title, "music_a": music_a, "music_b": music_b, "music_finale": music_finale}


if __name__ == "__main__":
    args = sys.argv[1:]
    want_sfx = not args or "sfx" in args
    want_music = not args or "music" in args
    for name, (fn, peak) in SFX.items():
        if want_sfx or name in args:
            write(name, fn(), peak)
    for name, fn in MUSIC.items():
        if want_music or name in args:
            fn()
