import random
from math import lcm
from kit import *
from fam_cross import BEAMS

# Two-lane family: out along the top lane, back along the bottom one (a U), each lane with its
# own obstacle, or one loop block whose track runs through both. Dials in either lane. Keeps
# layouts that need a loan (and, when DEBT, the debt).
WANT = dict(borrow=True, debt=True, margin=3, par=(140, 640))
SEED = 1200
N = 240
CH = 12


def build(i, spec):
    W = 16
    A, B = 2, 6
    rows = [[" "] * W for _ in range(9)]
    for x in range(1, W - 1):
        rows[A][x] = "."
        rows[B][x] = "."
    turn_x = spec["turn"]
    for y in range(A + 1, B):
        rows[y][turn_x] = "."
    rows[0][1] = "."
    rows[1][1] = "S"
    rows[0][2] = "."
    rows[1][2] = "."
    ex_x = 1 if turn_x > 7 else W - 2
    rows[B + 1][ex_x] = "."
    rows[B + 2][ex_x] = "X"
    sliders, lasers = [], []
    periods = []
    for lane_y, ob in ((A, spec["a"]), (B, spec["b"])):
        if ob is None:
            continue
        kind = ob[0]
        if kind == "beam":
            on, off, ph = ob[1:]
            if turn_x > 7:
                rows[lane_y][0] = "#"
                lasers.append(laser((0, lane_y), "E", on=on, off=off, phase=ph))
            else:
                rows[lane_y][W - 1] = "#"
                lasers.append(laser((W - 1, lane_y), "W", on=on, off=off, phase=ph))
            periods.append(on + off if off else 1)
        elif kind == "slide":
            a, b, sp, ph = ob[1:]
            sliders.append(slider([(a, lane_y), (b, lane_y)], speed=sp, phase=ph))
            periods.append(2 * (b - a) * sp)
    if spec.get("loop"):
        x0, x1, sp, ph, d = spec["loop"]
        for y in range(A + 1, B):
            rows[y][x0] = "."
            rows[y][x1] = "."
        sliders.append(slider([(x0, A), (x1, A), (x1, B), (x0, B)], loop=True, speed=sp, phase=ph, dir=d))
        periods.append(2 * ((x1 - x0) + (B - A)) * sp)
    for (x, y) in spec["dials"]:
        rows[y][x] = "L"
    rows = ["".join(r) for r in rows]
    lv = trim(level(f"{CH}-x", f"u{i}", CH, rows, sliders=sliders, lasers=lasers, term=spec["term"], loans=spec["loans"]))
    c = 1
    for p in periods:
        c = lcm(c, p)
    return lv, c


def lane_ob(rng, y):
    k = rng.choice(["beam", "slide", "slide", None])
    if k == "beam":
        on, off = rng.choice([b for b in BEAMS if b[1]])
        return ("beam", on, off, rng.randrange(0, on + off))
    if k == "slide":
        a = rng.choice([1, 2, 3, 4])
        b = rng.choice([9, 11, 13, 14])
        return ("slide", a, b, rng.choice([2, 3, 4]), rng.randrange(0, 20))
    return None


def candidates():
    rng = random.Random(SEED)
    i = 0
    while i < N:
        spec = {"turn": 14, "a": lane_ob(rng, 2), "b": lane_ob(rng, 6), "loop": None,
                "term": rng.choice(TERMS), "loans": rng.choice(LOANS)}
        if rng.random() < 0.35:
            x0 = rng.choice([3, 4, 5, 6])
            x1 = x0 + rng.choice([3, 4, 5, 6])
            if x1 < 13 and x0 > 1:
                spec["loop"] = (x0, x1, rng.choice([2, 3]), rng.randrange(0, 20), rng.choice([1, -1]))
        if spec["a"] is None and spec["b"] is None and spec["loop"] is None:
            continue
        nd = rng.choice(DIALS)
        spec["dials"] = [(rng.randrange(3, 13), rng.choice([2, 6])) for _ in range(nd)]
        if len(set(spec["dials"])) < nd:
            continue
        try:
            lv, cyc = build(i, spec)
        except Exception:
            continue
        if cyc > 240:
            continue
        i += 1
        yield f"u{i} {spec}", lv


TERMS = [60, 80, 100, 120]
LOANS = [1, 2, 2]
DIALS = [1, 2, 2]
