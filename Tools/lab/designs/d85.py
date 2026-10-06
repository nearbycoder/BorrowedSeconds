from kit import *

# 8-x Amortize, bridge loan: a dead-end corridor whose shuttle covers it end to end (Pass-Through:
# only a frozen you lets it by), then a long lane swept by a fast shuttle (First Loan: freeze it in
# its alcove). The first loan's debt carries you through the corridor; the lane needs a second.
WANT = dict(borrow=True, debt=True, margin=3, par=(160, 600), min_loans=2)


def make(p):
    rows = [" ...            ",
            " .S.            ",
            " ...            ",
            "  .             ",
            "  .........     ",
            "                ",
            "                ",
            "                ",
            "                ",
            "                "]
    rows = [list(r) for r in rows]
    for x in range(0, p["len"]):
        rows[7][x] = "."
    for y in (5, 6):
        rows[y][10] = "."
    rows[7][10] = "."
    rows[8][p["leave"]] = "."
    rows[9][p["leave"]] = "X"
    rows = ["".join(r) for r in rows]
    lv = level("8-x", "Bridge", 8, rows,
               sliders=[slider([(2, 4), (10, 4)], speed=p["sa"], dwell=p["dw"], phase=p["pa"]),
                        slider([(0, 7), (p["len"] - 1, 7)], speed=p["sb"], phase=p["pb"])],
               term=p["term"], loans=2)
    return trim(lv)


def candidates():
    for p in sample(18, seed=85, len=[14, 16], leave=[1, 2, 3], sa=[4, 5], dw=[8], pa=[0, 10],
                    sb=[1, 2], pb=[0, 9], term=[60, 80, 100]):
        yield label(p), make(p)
