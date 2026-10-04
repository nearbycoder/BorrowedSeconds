from kit import *

# 5-4 Pendulum: one frozen hand, two jobs. Lying east it crosses the beam column and the chute
# beside it: the crystal arm shades the beam below it and turns the chute block back above it.
WANT = dict(borrow=True, margin=3, par=(100, 420))


def make(p):
    n = p["n"]
    py = 3
    rows = []
    for y in range(n + 2):
        r = [" "] * 9
        if y == 0:
            r[4] = "#"
        if 1 <= y <= n:
            r[5] = "."
            if y > py:
                r[4] = "."
        if y == py:
            r[2] = "."
        if y == n + 1:
            r[4] = "#"
        if y == n:
            r[3] = "X"
        rows.append("".join(r))
    # start east of the chute, joining it below the arm
    sy = py + p["join"]
    rows = [list(r.ljust(10)) for r in rows]
    rows[sy][6] = "."
    for y in (sy - 1, sy, sy + 1):
        rows[y][7] = "."
        rows[y][8] = "."
    rows[sy][8] = "S"
    rows = ["".join(r) for r in rows]
    lv = level("5-4", "Pendulum", 5, rows,
               sliders=[slider([(5, 1), (5, n)], speed=p["ss"], phase=p["sp"])],
               lasers=[laser((4, 0), "S", on=p["on"], off=p["off"], phase=p["lp"])],
               rotors=[rotor((2, py), "E", 3, cw=p["cw"], turn=p["turn"], hold=p["hold"], phase=p["rp"])],
               term=100, loans=1,
               hint="A frozen hand is a crystal wall: it shades the light below it and turns the block back above it.")
    return trim(transpose(lv))


def intended(tr, lv):
    b = tr.borrows()
    # frozen at rest: the arm is a single crystal line, so only one floor tile (the chute) is crystal
    return len(b) == 1 and b[0][1] == "B2" and due_at_exit(tr, lv) and len(tr.find(b[0][4], "%")) == 1


def candidates():
    for p in sample(110, seed=541, n=[9, 10], join=[2, 3], ss=[2, 3], sp=[0, 7, 13], on=[20, 40], off=[0, 0, 20],
                    lp=[0, 10], cw=[True, False], turn=[3, 4], hold=[8, 12, 20], rp=[0, 10, 20]):
        yield label(p), make(p)
