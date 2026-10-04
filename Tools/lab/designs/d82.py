import random
from kit import *
from fam_cross import build, cycle, BEAMS, TIMINGS

# 8-x Amortize family: crossings that provably need two loans.
WANT = dict(borrow=True, margin=3, par=(160, 600), min_loans=2)
SEED = 82
N = 200
TERMS = [60, 80, 100]


def random_slots(rng):
    xs = rng.sample(range(4, 14), rng.choice([3, 4, 4]))
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
        loans = 2
        ex = rng.choice(["e", "s", "n"])
        lv = build("8-x", f"a{i}", 8, slots, term=term, loans=loans, lane_laser=lane, exit_side=ex)
        yield f"a{i} term={term} loans={loans} {slots} lane={lane} exit={ex}", lv
