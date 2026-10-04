from kit import *

# 5-5 Fulcrum: the Rebound pen again, but the chute holds a dial. The pen buys the walk down;
# the debt has to land you on the dial so the chute block can pass through you while it latches.
WANT = dict(borrow=True, debt=True, margin=3, par=(140, 500))


def make(p):
    top, n, k = 3, p["chute"], p["dial"]
    rows = []
    for y in range(n + 2):
        r = [" "] * 10
        if y <= n: r[6] = "."
        if y == n + 1: r[6] = "X"
        if y == k: r[6] = "L"
        if y == top: r[1:7] = "......"
        if y == top - 1: r[1:4] = ".S."
        if y == top - 2: r[1:4] = "..."
        if y == top + 1: r[5] = "."
        if p["side"] and y == k: r[7] = "."
        rows.append("".join(r))
    lv = level("5-5", "Fulcrum", 5, rows,
               sliders=[slider([(3, top), (6, top)], speed=p["sa"], dwell=p["da"], phase=p["pa"]),
                        slider([(6, 0), (6, n)], speed=p["sb"], phase=p["pb"])],
               term=p["term"], loans=1,
               hint="One loan opens the way. The debt has to hold the dial.")
    return trim(transpose(lv))


def candidates():
    for p in sample(220, seed=56, chute=[11, 12], dial=[7, 8, 9, 10], side=[0, 1], sa=[2, 3], da=[0, 8],
                    pa=[0, 5], sb=[2, 3], pb=[0, 7, 13], term=[60, 80, 100, 120]):
        yield label(p), make(p)


def intended(tr, lv):
    b = tr.borrows()
    return len(b) == 1 and b[0][1] == "B0" and len(due_on(tr, lv)) == 1
