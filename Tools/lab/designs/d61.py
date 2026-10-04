from kit import *

# 6-1 Lockout: a closed gate is a wall to blocks as well. The lane slider is penned between the
# west end and gate A; hold plate a while it runs past into the east pen, then let the gate shut
# behind it and the lane is yours. The plate sits where only a frozen (debt) or lucky body can be.
WANT = dict(borrow=True, margin=3, par=(120, 480))


def make(p):
    W = 15
    lane = 4
    rows = [[" "] * W for _ in range(8)]
    # start pocket above the lane entry
    for y in range(0, 3):
        for x in range(p["ent"] - 1, p["ent"] + 2):
            rows[y][x] = "."
    rows[1][p["ent"]] = "S"
    rows[3][p["ent"]] = "."
    for x in range(1, W - 1):
        rows[lane][x] = "."
    g = p["gate"]
    rows[lane][g] = "A"
    # plate: on the lane (west part) or in a pocket below it
    px, py = p["plate"]
    rows[py][px] = "a"
    if py != lane:
        for y in range(lane + 1, py):
            rows[y][px] = "."
    # exit below the lane just west of the gate
    rows[lane + 1][g - 1] = "."
    rows[lane + 2][g - 1] = "X"
    rows = ["".join(r) for r in rows]
    lasers = []
    if p["laser"]:
        # a blinking beam across the plate from the west wall
        rows = [list(r) for r in rows]
        rows[py][0] = "#"
        if py != 4:
            rows[py][px + 1] = "#"
        rows = ["".join(r) for r in rows]
        lasers.append(laser((0, py), "E", on=p["on"], off=p["off"], phase=p["lp"]))
    lv = level("6-1", "Lockout", 6, rows,
               sliders=[slider([(1, lane), (W - 2, lane)], speed=p["s"], phase=p["ph"])],
               lasers=lasers, term=p["term"], loans=p["loans"],
               hint="A closed gate is a wall to blocks too. Let it through, then shut it.")
    return trim(lv)


def candidates():
    for p in sample(240, seed=63, ent=[2, 3, 4], gate=[9, 10, 11], plate=[(1, 4), (2, 4), (3, 4), (1, 6), (2, 6), (5, 6)],
                    s=[2, 3], ph=[0, 4, 9, 15, 22], term=[60, 80, 100], loans=[1], laser=[0, 1], on=[20, 30], off=[10, 20], lp=[0, 13]):
        if p["plate"][1] == 4 and p["plate"][0] == p["ent"]:
            continue
        if p["plate"][1] == 6 and abs(p["plate"][0] - (p["gate"] - 1)) < 2:
            continue
        if not p["laser"] and (p["on"], p["off"], p["lp"]) != (20, 10, 0):
            continue
        yield label(p), make(p)


def intended(tr, lv):
    b = tr.borrows()
    if len(b) != 1 or b[0][0] < 15:
        return False
    gx = tiles_of(lv, "A")[0][0]
    last = [e for e in tr.events if e[4]][-1][4]
    hs = tr.find(last, "Hh")
    return bool(hs) and all(x > gx for x, y in hs)
