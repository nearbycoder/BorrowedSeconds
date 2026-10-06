#!/usr/bin/env python3
"""Measures the music/effects balance of a real play session from the game's audio event log
(AudioDirector.Log, written by Tools/checks.sh as Builds/checks/audio.log).

    balance.py AUDIO.log [--min-median 6] [--min-each 3] [--max-peak 0.9]

Renders the music and the one-shots as separate stems with Tools/demo/mix.py (the same mixer
the demo and trailer soundtracks use, mirroring AudioDirector), then for every key gameplay
stinger reports how far the effects sit above the music in the 400 ms after it fires (RMS, dB).
Also prints the music bed level between stingers and the mixed peak. Exits non-zero if the
median or any single stinger falls short. These are numbers, not ears: listen as well.
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "demo"))
import mix  # noqa: E402

KEY = ["borrow", "freeze", "thaw", "death", "lock_latch", "exit_open", "win"]
WINDOW = 0.4


def db(x):
    return 20 * np.log10(max(float(np.sqrt(np.mean(np.square(x)))), 1e-9))


def base(clip):
    head, _, tail = clip.rpartition("_")
    return head if tail.isdigit() else clip


def main():
    args = sys.argv[1:]
    path = args[0]
    min_median = float(args[args.index("--min-median") + 1]) if "--min-median" in args else 6.0
    min_each = float(args[args.index("--min-each") + 1]) if "--min-each" in args else 3.0
    max_peak = float(args[args.index("--max-peak") + 1]) if "--max-peak" in args else 0.9
    mus, world, ui, shots = mix.stems(path)
    fx = world + ui
    sr = mix.SR
    w = int(WINDOW * sr)
    rows, quiet = [], np.ones(len(mus), bool)
    for t, clip, vol, pitch, is_world in shots:
        s = int(round(t * sr))
        quiet[max(0, s):s + w] = False
        name = base(clip)
        if name not in KEY or s < 0 or s + w > len(mus):
            continue
        rows.append((name, t, db(fx[s:s + w]), db(mus[s:s + w])))
    if not rows:
        sys.exit("no key stingers in the log")
    print(f"{'stinger':<11} {'at':>7} {'fx dB':>7} {'music dB':>9} {'fx-music':>9}")
    for name, t, f, m in rows:
        print(f"{name:<11} {t:7.2f} {f:7.1f} {m:9.1f} {f - m:+9.1f}")
    gaps = np.array([f - m for _, _, f, m in rows])
    per = {}
    for name, _, f, m in rows:
        per.setdefault(name, []).append(f - m)
    print("per stinger (median fx-music dB): " + ", ".join(f"{k} {np.median(v):+.1f}" for k, v in per.items()))
    out = mus + fx
    bed = db(mus[quiet]) if quiet.any() else float("nan")
    peak = float(np.abs(out).max())
    print(f"music bed between stingers {bed:.1f} dBFS RMS; effects peak {np.abs(fx).max():.2f}, music peak {np.abs(mus).max():.2f}")
    median, worst = float(np.median(gaps)), float(gaps.min())
    ok = median >= min_median and worst >= min_each and peak <= max_peak
    print(f"{'PASS' if ok else 'FAIL'} audio-balance: {len(rows)} stingers, median {median:+.1f} dB (want >= {min_median:+.0f}), "
          f"worst {worst:+.1f} dB (want >= {min_each:+.0f}), mixed peak {peak:.2f} (want <= {max_peak:.2f})")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
