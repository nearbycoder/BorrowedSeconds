from kit import *

# 8-x Amortize, two shadows: one chute crosses two lanes, each lit by an always-on beam from the
# west. Each lane is walked in a shadow, which lasts one loan, and the connector between them is
# where the first debt gets paid. (Designed by hand; the solver's route shades the first lane by
# freezing its laser while the chute blocks it, and freezes the chute itself in the second.)
WANT = dict(borrow=True, margin=3, par=(160, 600), min_loans=2)


def make(speed=3, phase=0, term=60):
    W, H = 16, 11
    rows = [[" "] * W for _ in range(H)]
    for y in (3, 7):
        for x in range(5, 15):
            rows[y][x] = "."
        rows[y][4] = "#"
    for y in range(1, 10):
        rows[y][5] = "."
    for y in range(0, 3):
        for x in range(7, 10):
            rows[y][x] = "."
    rows[1][8] = "S"
    for y in range(4, 7):
        rows[y][14] = "."
    rows[8][6] = "X"
    rows = ["".join(r) for r in rows]
    lv = level("8-x", "Shade", 8, rows, sliders=[slider([(5, 1), (5, 9)], speed=speed, phase=phase)],
               lasers=[laser((4, 3), "E", on=20, off=0), laser((4, 7), "E", on=20, off=0)], term=term, loans=2)
    return trim(lv)


def candidates():
    yield "speed=3 phase=0 term=60", make()
