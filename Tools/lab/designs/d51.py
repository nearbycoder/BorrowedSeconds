from kit import *

# 5-1 Rebound: a frozen block is a wall to other blocks. The bar slider A crosses the top of
# the long chute; freeze it on the crossing while the chute slider B is above it and B is
# penned up there, so one loan clears both the bar's lane and the chute.
WANT = dict(borrow=True, margin=3, par=(100, 400))


def make(p):
    top, n = p["top"], p["chute"]
    rows = []
    for y in range(n + 2):
        r = [" "] * 9
        if y <= n: r[6] = "."
        if y == n + 1: r[6] = "X"
        if y == top: r[1:7] = "......"
        if y == top - 1: r[1:4] = ".S."
        if y == top - 2 and top >= 2: r[1:4] = "..."
        if y == top + 1: r[5] = "."
        rows.append("".join(r))
    return level("5-1", "Rebound", 5, rows,
                 sliders=[slider([(3, top), (6, top)], speed=p["sa"], dwell=p["da"], phase=p["pa"]),
                          slider([(6, 0), (6, n)], speed=p["sb"], phase=p["pb"])],
                 term=p["term"], loans=1,
                 hint="Crystal is solid to everything. A frozen block turns other blocks back.")


def intended(tr):
    b = tr.borrows()
    if len(b) != 1 or b[0][1] != "B0":
        return False
    hs = tr.find(b[0][4], "Hh")
    fy = tr.find(b[0][4], "Ff")[0][1]
    return hs and all(y < fy for x, y in hs)


def candidates():
    for p in grid(top=[2, 3], chute=[11, 12], sa=[2, 3], da=[0, 8], pa=[0, 5], sb=[2, 3], pb=[0, 7], term=[100]):
        yield label(p), make(p)
