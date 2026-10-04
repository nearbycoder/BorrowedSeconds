from kit import *

# 7-1 Gatekeeper: a closed gate stops a beam. The lane past gate A is dark while the gate is
# shut, but the loop block keeps stepping on plate a and flooding it with light. Freeze the
# block off the plate and walk the shaded lane.
WANT = dict(borrow=True, margin=3, par=(100, 420))


def make(p):
    rows = [
        "     ....       ",
        "     .##.       ",
        "     ....       ",
        "                ",
        "#....A........# ",
        "       .        ",
        "      .S.       ",
        "      ...       ",
    ]
    rows = [list(r) for r in rows]
    for (x, y) in p["plates"]:
        rows[y][x] = "a"
    ex = p["exit"]
    if ex == "end":
        rows[4][13] = "X"
    else:
        rows[3][13] = "X"
    rows = ["".join(r) for r in rows]
    lv = level("7-1", "Gatekeeper", 7, rows,
               sliders=[slider([(5, 0), (8, 0), (8, 2), (5, 2)], loop=True, speed=p["s"], phase=p["ph"], dir=p["dir"])],
               lasers=[laser((0, 4), "E", on=p["on"], off=p["off"], phase=p["lp"])],
               term=p["term"], loans=1,
               hint="A shut gate is a shield. Every time the block steps on the plate, the light gets through.")
    return trim(lv)


def candidates():
    plate_sets = [[(8, 1)], [(5, 1), (8, 1)], [(6, 0), (7, 2)], [(8, 0), (5, 2)], [(6, 0), (8, 1), (6, 2)]]
    for p in sample(200, seed=72, plates=plate_sets, s=[2, 3, 4], ph=[0, 4, 9, 14, 21], dir=[1, -1], exit=["side"],
                    on=[20, 40], off=[0, 0, 20], lp=[0, 10], term=[60, 100]):
        if p["off"] == 0 and p["lp"]:
            continue
        yield label(p), make(p)


def intended(tr, lv):
    b = tr.borrows()
    return len(b) == 1 and b[0][0] >= 15
