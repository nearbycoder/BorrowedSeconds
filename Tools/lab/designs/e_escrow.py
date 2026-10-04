from kit import *

# 6-3 Escrow: a gate can't close on anything standing in it, and that includes a frozen you.
# The lane block opens gate A for a moment as it rolls over plate a. Step into the gate from
# the side while it is open and stay: the debt freezes you there, the block passes through you
# into the pen, and when you step back out the gate shuts behind it. Then the lane is yours for
# the long walk to the dial pocket and back.
WANT = dict(borrow=True, debt=True, margin=3, par=(200, 640))


def make(p):
    W = 16
    lane = 3
    g, d = p["gate"], p["dial"]
    pl = g - 2
    rows = [[" "] * W for _ in range(7)]
    for x in range(1, W - 1):
        rows[lane][x] = "."
    for y in range(0, 3):
        for x in range(0, 3):
            rows[y][x] = "."
    rows[1][1] = "S"
    rows[2][1] = "."
    rows[lane][g] = "A"
    rows[lane][pl] = "a"
    rows[lane + 1][d] = "L"
    # side pocket beside the gate
    sy = lane + 1 if p["side"] == "s" else lane - 1
    rows[sy][g] = "."
    # exit off the lane west of the gate
    rows[lane + 1][p["ex"]] = "."
    rows[lane + 2][p["ex"]] = "X"
    rows = ["".join(r) for r in rows]
    lasers = []
    lv = level("6-3", "Escrow", 6, rows,
               sliders=[slider([(1, lane), (W - 2, lane)], speed=p["s"], phase=p["ph"])],
               lasers=lasers, term=p["term"], loans=1,
               hint="A gate can't close on anything standing in it. Even you, frozen.")
    return trim(lv)


def intended(tr, lv):
    gx = tiles_of(lv, "A")[0][0]
    last = [e for e in tr.events if e[4]][-1][4]
    hs = tr.find(last, "Hh")
    dues = [e for e in tr.events if "Due(" in e[3]]
    return bool(hs) and all(x > gx for x, y in hs) and len(dues) == 1 and dues[0][2][0] == gx


def candidates():
    for p in sample(200, seed=63, gate=[11, 12, 13], dial=[8, 9, 10], side=["n", "s"], ex=[2, 3, 4],
                    s=[2, 3, 4], ph=[0, 5, 11, 17], term=[60, 80, 100, 120]):
        if p["dial"] >= p["gate"] - 1 or (p["side"] == "s" and p["dial"] == p["gate"]):
            continue
        yield label(p), make(p)
