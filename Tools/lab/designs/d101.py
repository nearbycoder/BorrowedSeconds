import random
from kit import *
from fam_cross import build, cycle, BEAMS, TIMINGS

# 10-x Maturity family: lane crossings with a long term (8-12 s), where the debt has to be part of the plan.
WANT = dict(borrow=True, debt=True, margin=3, par=(200, 640))
SEED = 101
N = 200
TERMS = [160, 180, 200, 240]


def random_slots(rng):
    xs = rng.sample(range(4, 14), rng.choice([2, 3, 3, 4]))
    xs.sort()
    slots = []
    for x in xs:
        k = rng.choice(["chute", "chute", "beam", "rotor", "dial"])
        if k == "chute":
            slots.append(("chute", x, rng.choice([2, 3, 4]), rng.randrange(0, 20), rng.choice([0, 0, 1, 2])))
        elif k == "beam":
            on, off = rng.choice(BEAMS)
            slots.append(("beam", x, on, off, rng.randrange(0, 40) if off else 0))
        elif k == "rotor":
            turn, hold = rng.choice(TIMINGS)
            slots.append(("rotor", x, rng.choice("ns"), rng.choice(["N", "NS", "E"]), rng.choice([1, 2]),
                          turn, hold, rng.randrange(0, 20), rng.choice([True, False])))
        else:
            slots.append(("dial", x))
    # at most one dial-free design in three: dials are what the debt is for
    if not any(s[0] == "dial" for s in slots) and rng.random() < 0.7:
        free = [x for x in range(4, 14) if x not in xs]
        slots.append(("dial", rng.choice(free)))
    return slots


def candidates():
    rng = random.Random(SEED)
    i = 0
    while i < N:
        slots = random_slots(rng)
        lane = (rng.choice([20, 30]), rng.choice([10, 20]), rng.randrange(0, 30)) if rng.random() < 0.2 else None
        if cycle(slots, lane) > 120:
            continue
        i += 1
        term = rng.choice(TERMS)
        loans = rng.choice([1, 1, 2])
        ex = rng.choice(["e", "s", "n"])
        lv = build("10-x", f"m{i}", 10, slots, term=term, loans=loans, lane_laser=lane, exit_side=ex)
        yield f"m{i} term={term} loans={loans} {slots} lane={lane} exit={ex}", lv
