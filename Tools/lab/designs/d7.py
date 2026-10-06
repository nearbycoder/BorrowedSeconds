import random
from kit import *
from fam_cross import build, cycle
from d91 import random_slots, TERMS

# Chapter VII (Overdraft) fill-ins: the 9-x short-term lane-crossing family, sampled afresh for
# two more levels beside Same Day, Short Notice and Payroll (the strip designs). Keeps layouts
# that cross the lane with at least one moving hazard (chute or rotor) and a dial, so the new
# levels don't look like the strips, and that prove both borrow and debt.
WANT = dict(borrow=True, debt=True, margin=3, par=(120, 360))
SEED = 701
N = 160


def candidates():
    rng = random.Random(SEED)
    i = 0
    while i < N:
        slots = random_slots(rng)
        kinds = {s[0] for s in slots}
        if "dial" not in kinds or not kinds & {"chute", "rotor"}:
            continue
        lane = (rng.choice([20, 30]), rng.choice([10, 20]), rng.randrange(0, 30)) if rng.random() < 0.2 else None
        if cycle(slots, lane) > 120:
            continue
        i += 1
        term = rng.choice(TERMS)
        loans = rng.choice([1, 1, 2])
        ex = rng.choice(["e", "s", "n"])
        lv = build("7-x", f"v{i}", 7, slots, term=term, loans=loans, lane_laser=lane, exit_side=ex)
        yield f"v{i} term={term} loans={loans} {slots} lane={lane} exit={ex}", lv
