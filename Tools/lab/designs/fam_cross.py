"""Family generator: a main lane from a start pocket to the exit, crossed by hazards placed in
slots. Each slot is one of
    ("chute", x, speed, phase, half)  a vertical slider track through the lane (half: 0 full,
                                      1 only north of the lane +1, 2 only south)
    ("beam",  x, on, off, phase)      a laser on the north wall firing south across the lane
    ("rotor", x, side, arms, len, turn, hold, phase, cw)  a pivot north/south of the lane
    ("dial",  x)                      a time-lock on the lane
The lane itself can be a beam lane: lane_laser=(on, off, phase) fires east from the west wall.
"""
from kit import level, slider, laser, rotor, trim

H = 11
LANE = 5


def build(id, name, chapter, slots, width=16, term=100, loans=None, hint="", lane_laser=None, start="w", exit_side="e"):
    W = width
    rows = [[" "] * W for _ in range(H)]
    x0 = 1
    for x in range(x0, W - 1):
        rows[LANE][x] = "."
    # start pocket on the west end, above the lane
    for y in range(LANE - 3, LANE):
        for x in range(0, 3):
            rows[y][x] = "."
    rows[LANE - 2][1] = "S"
    rows[LANE][0] = " "
    ex = W - 2
    if exit_side == "e":
        rows[LANE][W - 1] = "X"
    elif exit_side == "s":
        rows[LANE + 1][ex] = "."
        rows[LANE + 2][ex] = "X"
    else:
        rows[LANE - 1][ex] = "."
        rows[LANE - 2][ex] = "X"
    sliders, lasers, rotors = [], [], []
    if lane_laser:
        on, off, ph = lane_laser
        rows[LANE][0] = "#"
        if exit_side == "e":
            rows[LANE][W - 1] = "X"
        lasers.append(laser((0, LANE), "E", on=on, off=off, phase=ph))
    for s in slots:
        kind = s[0]
        if kind == "chute":
            _, x, speed, ph, half = s
            # move counts are 10 (full) or 5 (half) so periods are 20*speed or 10*speed ticks
            y0, y1 = (0, H - 1) if half == 0 else ((1, LANE + 1) if half == 1 else (LANE - 1, H - 2))
            for y in range(y0, y1 + 1):
                if rows[y][x] == " ":
                    rows[y][x] = "."
            sliders.append(slider([(x, y0), (x, y1)], speed=speed, phase=ph))
        elif kind == "beam":
            _, x, on, off, ph = s
            rows[LANE - 3][x] = "#"
            rows[LANE + 3][x] = "#"
            lasers.append(laser((x, LANE - 3), "S", on=on, off=off, phase=ph))
        elif kind == "rotor":
            _, x, side, arms, ln, turn, hold, ph, cw = s
            py = LANE - ln if side == "n" else LANE + ln
            if rows[py][x] == " ":
                rows[py][x] = "."
            rotors.append(rotor((x, py), arms, ln, cw=cw, turn=turn, hold=hold, phase=ph))
        elif kind == "dial":
            rows[LANE][s[1]] = "L"
    rows = ["".join(r) for r in rows]
    return trim(level(id, name, chapter, rows, sliders=sliders, lasers=lasers, rotors=rotors, term=term, loans=loans, hint=hint))


def period_of(slot):
    from math import lcm
    k = slot[0]
    if k == "chute":
        return (20 if slot[4] == 0 else 10) * slot[2]
    if k == "beam":
        return 1 if slot[3] == 0 else slot[2] + slot[3]
    if k == "rotor":
        _, x, side, arms, ln, turn, hold, ph, cw = slot
        sym = 2 if arms in ("NS", "EW") else 1
        return (4 // sym) * (turn + hold)
    return 1


def cycle(slots, lane_laser=None):
    """Least common multiple of every obstacle's period: the size of the time loop the solver sees."""
    from math import lcm
    c = 1
    for sl in slots:
        c = lcm(c, period_of(sl))
    if lane_laser and lane_laser[1]:
        c = lcm(c, lane_laser[0] + lane_laser[1])
    return c


TIMINGS = [(3, 7), (4, 6), (3, 12), (5, 10), (4, 16), (5, 15)]
BEAMS = [(20, 0), (10, 10), (20, 10), (10, 20), (20, 20), (30, 10), (40, 20), (30, 30), (20, 40)]
