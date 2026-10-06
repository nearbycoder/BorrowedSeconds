import random
from kit import *
from fam_cross import build, cycle

# 8-x Amortize, two guarded dials: a blinking beam runs down the lane, and each of its two dials
# has a second guard of its own (a chute through one, a rotor arm over the other). Freezing one
# guard leaves the other, so only your frozen body can hold a dial for three seconds: each dial
# costs a debt, and two debts cost two loans.
WANT = dict(borrow=True, debt=True, margin=3, par=(160, 600), min_loans=2)
SEED = 84
N = 24


def candidates():
    rng = random.Random(SEED)
    seen = set()
    i = 0
    while i < N:
        d1 = rng.choice([4, 5, 6])
        d2 = rng.choice([9, 10, 11])
        turn, hold = rng.choice([(3, 7), (4, 6), (5, 15), (4, 16)])
        slots = [("chute", d1, rng.choice([2, 3]), rng.randrange(0, 20), 0), ("dial", d1),
                 ("rotor", d2, rng.choice("ns"), "N", 2, turn, hold, rng.randrange(0, 20), rng.choice([True, False])), ("dial", d2)]
        on, off = rng.choice([(20, 20), (10, 10), (30, 10), (20, 10), (10, 30)])
        lane = (on, off, rng.randrange(0, on + off))
        if cycle(slots, lane) > 240:
            continue
        term = rng.choice([60, 80, 100])
        ex = rng.choice(["e", "s", "n"])
        key = (repr(slots), lane, term, ex)
        if key in seen:
            continue
        seen.add(key)
        i += 1
        lv = build("8-x", f"g{i}", 8, slots, term=term, loans=2, lane_laser=lane, exit_side=ex)
        yield f"g{i} term={term} {slots} lane={lane} exit={ex}", lv
