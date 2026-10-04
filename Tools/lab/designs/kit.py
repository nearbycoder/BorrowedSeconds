"""Helpers for level design sweeps (see Tools/lab/sweep.py)."""
import itertools


def level(id, name, chapter, rows, sliders=(), lasers=(), rotors=(), term=100, loans=None, hint=""):
    d = {"id": id, "name": name, "chapter": chapter, "term": term}
    if loans is not None:
        d["loans"] = loans
    d["hint"] = hint
    d["map"] = [r.rstrip() if r.strip() else r for r in rows]
    w = max(len(r) for r in d["map"])
    d["map"] = [r.ljust(w) for r in d["map"]]
    if sliders: d["sliders"] = [s for s in sliders if s]
    if lasers: d["lasers"] = [l for l in lasers if l]
    if rotors: d["rotors"] = [r for r in rotors if r]
    return d


def slider(path, loop=False, speed=4, dwell=0, start=0, dir=1, phase=0):
    s = {"path": [list(p) for p in path], "loop": loop, "speed": speed}
    if dwell: s["dwell"] = dwell
    if start: s["start"] = start
    if dir != 1: s["dir"] = dir
    if phase: s["phase"] = phase
    return s


def laser(at, dir, on=20, off=20, phase=0, plate=None):
    l = {"at": list(at), "dir": dir, "on": on, "off": off}
    if phase: l["phase"] = phase
    if plate: l["plate"] = plate
    return l


def rotor(at, arms="N", len=2, cw=True, turn=4, hold=16, phase=0):
    r = {"at": list(at), "arms": arms, "len": len, "cw": cw, "turn": turn, "hold": hold}
    if phase: r["phase"] = phase
    return r


def grid(**ranges):
    """Every combination of the given parameter lists, as dicts."""
    keys = list(ranges)
    for vals in itertools.product(*(ranges[k] for k in keys)):
        yield dict(zip(keys, vals))


def label(p):
    return " ".join(f"{k}={v}" for k, v in p.items())


_TDIR = {"N": "W", "W": "N", "E": "S", "S": "E"}


def transpose(lv):
    """Mirrors a level across its main diagonal (x <-> y): a tall design becomes a wide one.
    Directions swap N<->W and E<->S, and a reflection turns clockwise rotors anticlockwise."""
    import copy
    d = copy.deepcopy(lv)
    rows = lv["map"]
    w = max(len(r) for r in rows)
    rows = [r.ljust(w) for r in rows]
    d["map"] = ["".join(rows[y][x] for y in range(len(rows))).rstrip() for x in range(w)]
    w2 = max(len(r) for r in d["map"])
    d["map"] = [r.ljust(w2) for r in d["map"]]
    for s in d.get("sliders", []):
        s["path"] = [[p[1], p[0]] for p in s["path"]]
    for l in d.get("lasers", []):
        l["at"] = [l["at"][1], l["at"][0]]
        l["dir"] = _TDIR[l["dir"]]
    for r in d.get("rotors", []):
        r["at"] = [r["at"][1], r["at"][0]]
        r["arms"] = "".join(_TDIR[c] for c in r["arms"])
        r["cw"] = not r.get("cw", True)
    return d


def show(lv):
    print(lv["id"], lv["name"])
    print("\n".join("  |" + r + "|" for r in lv["map"]))


def trim(lv):
    """Drops empty border rows and columns, shifting every coordinate to match."""
    import copy
    d = copy.deepcopy(lv)
    rows = d["map"]
    ys = [y for y, r in enumerate(rows) if r.strip()]
    xs = [x for x in range(max(len(r) for r in rows)) if any(x < len(r) and r[x] != " " for r in rows)]
    y0, y1, x0, x1 = ys[0], ys[-1], xs[0], xs[-1]
    d["map"] = [r.ljust(x1 + 1)[x0:x1 + 1] for r in rows[y0:y1 + 1]]
    for s in d.get("sliders", []):
        s["path"] = [[p[0] - x0, p[1] - y0] for p in s["path"]]
    for k in ("lasers", "rotors"):
        for o in d.get(k, []):
            o["at"] = [o["at"][0] - x0, o["at"][1] - y0]
    return d


def save(lv, name):
    """Writes a finished design to Tools/lab/<name>.json (the file assemble.py reads)."""
    import json, os
    path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), name + ".json")
    json.dump(lv, open(path, "w"), indent=1)
    return path


def sample(count, seed=1, **ranges):
    """n random combinations of the given parameter lists (deterministic for a seed), no repeats."""
    import random
    rng = random.Random(seed)
    seen, out = set(), []
    total = 1
    for v in ranges.values():
        total *= len(v)
    while len(out) < min(count, total):
        p = {k: rng.choice(v) for k, v in ranges.items()}
        key = tuple(sorted((k, repr(v)) for k, v in p.items()))
        if key in seen:
            continue
        seen.add(key)
        out.append(p)
    return out


def exit_of(lv):
    for y, r in enumerate(lv["map"]):
        if "X" in r:
            return (r.index("X"), y)


def due_at_exit(tr, lv):
    """True when the (only) debt is paid standing on the exit: the route was finished on the loan."""
    dues = [e for e in tr.events if "Due(" in e[3]]
    return len(dues) == 1 and dues[0][2] == exit_of(lv)


def tiles_of(lv, ch):
    return [(x, y) for y, r in enumerate(lv["map"]) for x, c in enumerate(r) if c == ch]


def due_on(tr, lv, ch="L"):
    """Positions where debts landed that are on a map tile of the given kind (dials by default)."""
    spots = set(tiles_of(lv, ch))
    return [e[2] for e in tr.events if "Due(" in e[3] and e[2] in spots]
