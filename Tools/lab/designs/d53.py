from kit import *

# 5-3 Crossbar: freeze the beam while it is lit and its crystal bar is a wall across both
# chutes. Catch both blocks above it and one loan clears the two chutes you walk below.
WANT = dict(borrow=True, margin=3, par=(100, 400))


def make(p):
    n = p["n"]
    rows = ["   .   .  ", "   .   .  ", "#  .   .  ", "   .....  "] + ["   .   .  "] * (n - 3) + ["   S   X  "]
    beam_y = 2
    lv = level("5-3", "Crossbar", 5, rows,
               sliders=[slider([(3, 0), (3, n)], speed=p["s1"], phase=p["p1"]),
                        slider([(7, 0), (7, n)], speed=p["s2"], phase=p["p2"])],
               lasers=[laser((0, beam_y), "E", on=p["on"], off=p["off"], phase=p["lp"])],
               term=100, loans=1,
               hint="A beam frozen while lit leaves a crystal bar, and crystal turns blocks back.")
    return trim(transpose(lv))


def intended(tr, lv):
    b = tr.borrows()
    return len(b) == 1 and b[0][1] == "B2" and b[0][0] > 10 and due_at_exit(tr, lv)


def candidates():
    for p in grid(n=[11, 12], s1=[2, 3], s2=[2, 3], p1=[0, 6], p2=[0, 9], on=[30, 40], off=[20], lp=[0, 15]):
        yield label(p), make(p)
