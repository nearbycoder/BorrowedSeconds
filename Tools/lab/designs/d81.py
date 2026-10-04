from kit import *

# 8-1 Instalments: two dials, each under its own hazard, and only your frozen body can hold
# either one for three seconds. Two loans, two debts: plan where each one lands.
WANT = dict(borrow=True, margin=3, par=(200, 640), min_loans=2)


def make(p):
    rows = [
        " ...      .     ",
        " .S.      .     ",
        " ...      .     ",
        "  .       .     ",
        "  ........L.....",
        "              . ",
        "#.......L.......",
        "            X   ",
    ]
    rows = [list(r) for r in rows]
    rows[7][12] = " "
    for y in range(p["chute"] + 1):
        if rows[y][10] == " ":
            rows[y][10] = "."
    ex, ey = p["exit"]
    rows[ey][ex] = "X"
    rows = ["".join(r) for r in rows]
    lv = level("8-1", "Instalments", 8, rows,
               sliders=[slider([(10, 0), (10, p["chute"])], speed=p["s"], phase=p["ph"])],
               lasers=[laser((0, 6), "E", on=p["on"], off=p["off"], phase=p["lp"])],
               term=p["term"], loans=2,
               hint="Two dials, two debts. Each one has to land somewhere.")
    return trim(lv)


def candidates():
    # chute of 5 moves -> period 10*speed; the beam shares that period so the time loop stays short
    for p in sample(40, seed=81, chute=[5], s=[3, 4], ph=[0, 5, 11], split=[0.5, 0.33, 0.67],
                    lp=[0, 15, 30], term=[60, 80, 100], exit=[(15, 7), (12, 7), (15, 3)]):
        per = 10 * p["s"]
        p["on"] = int(per * p["split"]) // 5 * 5
        p["off"] = per - p["on"]
        yield label(p), make(p)
