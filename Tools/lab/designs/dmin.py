import random
from kit import *
from fam_cross import BEAMS, TIMINGS

# Minimal family: one lane, ONE obstacle, a few dials. The cleanest puzzles in the game are this
# shape (Same Day): everything on screen matters. Keeps layouts proving borrow + debt.
WANT = dict(borrow=True, debt=True, margin=3, par=(120, 640))
SEED = 900
N = 260


def build(i, spec):
    kind, dials, term, loans, ex = spec["kind"], spec["dials"], spec["term"], spec["loans"], spec["exit"]
    W, lane = 16, 3
    rows = [[" "] * W for _ in range(8)]
    for x in range(1, W - 1):
        rows[lane][x] = "."
    for y in range(0, 3):
        for x in range(0, 3):
            rows[y][x] = "."
    rows[1][1] = "S"
    sliders, lasers, rotors = [], [], []
    if kind == "beam":
        on, off, ph = spec["beam"]
        rows[lane][0] = "#"
        lasers.append(laser((0, lane), "E", on=on, off=off, phase=ph))
    elif kind == "lane":
        a, b, sp, ph = spec["lane"]
        sliders.append(slider([(a, lane), (b, lane)], speed=sp, phase=ph))
    elif kind == "loop":
        x0, x1, sp, ph, d = spec["loop"]
        for x in range(x0, x1 + 1):
            rows[lane + 2][x] = "."
        rows[lane + 1][x0] = "."
        rows[lane + 1][x1] = "."
        for x in range(x0 + 1, x1):
            rows[lane + 1][x] = "#"
        sliders.append(slider([(x0, lane), (x1, lane), (x1, lane + 2), (x0, lane + 2)], loop=True, speed=sp, phase=ph, dir=d))
    elif kind == "rotor":
        x, arms, ln, turn, hold, ph, cw = spec["rotor"]
        py = lane + ln if ln else lane + 1
        if rows[py][x] == " ":
            rows[py][x] = "."
        rotors.append(rotor((x, py), arms, ln, cw=cw, turn=turn, hold=hold, phase=ph))
    for x in dials:
        rows[lane][x] = "L"
    if ex == "e":
        rows[lane][W - 1] = "X"
    elif ex == "n":
        rows[lane - 1][W - 2] = "."
        rows[lane - 2][W - 2] = "X"
    rows = ["".join(r) for r in rows]
    return trim(level("m-x", f"m{i}", 9, rows, sliders=sliders, lasers=lasers, rotors=rotors, term=term, loans=loans))


def candidates():
    rng = random.Random(SEED)
    for i in range(N):
        kind = rng.choice(["beam", "beam", "lane", "loop", "rotor"])
        nd = rng.choice([1, 2, 2, 3])
        dials = sorted(rng.sample(range(3, 14), nd))
        spec = {"kind": kind, "dials": dials, "term": rng.choice([30, 40, 60, 100, 160, 200]),
                "loans": rng.choice([1, 2, 2, 3]), "exit": rng.choice(["e", "n"])}
        if kind == "beam":
            on, off = rng.choice([b for b in BEAMS if b[1]])
            spec["beam"] = (on, off, rng.randrange(0, on + off))
        elif kind == "lane":
            a = rng.choice([1, 2, 3])
            b = rng.choice([10, 12, 14])
            spec["lane"] = (a, b, rng.choice([2, 3, 4]), rng.randrange(0, 20))
        elif kind == "loop":
            x0 = rng.choice([3, 4, 5, 6])
            x1 = x0 + rng.choice([4, 5, 6, 7])
            if x1 > 14:
                continue
            spec["loop"] = (x0, x1, rng.choice([2, 3, 4]), rng.randrange(0, 20), rng.choice([1, -1]))
        else:
            turn, hold = rng.choice(TIMINGS)
            spec["rotor"] = (rng.choice(range(4, 13)), rng.choice(["N", "NS", "NE"]), rng.choice([1, 2]), turn, hold,
                             rng.randrange(0, 20), rng.choice([True, False]))
        try:
            lv = build(i, spec)
        except Exception:
            continue
        yield f"m{i} {spec}", lv
