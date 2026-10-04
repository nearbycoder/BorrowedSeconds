import random
from kit import *
from fam_gate import build

# Escrow family sampler, debt variant: layouts where your own freeze is part of the answer.
WANT = dict(borrow=True, debt=True, margin=3, par=(140, 560))
SEED, N, DEBT = 63, 260, True


def spec(rng):
    s = {}
    ng = rng.choice([1, 1, 2])
    gx = sorted(rng.sample(range(5, 13), ng))
    chans = ["a", "b"][:ng]
    s["gates"] = list(zip(gx, chans))
    plates = []
    if rng.random() < 0.8:
        x0 = rng.choice([4, 6, 8, 10])
        side = rng.choice("ns")
        k = rng.choice([1, 1, 2])
        idxs = rng.sample(range(10), k)
        plates = [(i, rng.choice(chans)) for i in idxs]
        s["ring"] = (x0, side, rng.choice([2, 3, 4]), rng.randrange(0, 20), rng.choice([1, -1]), plates)
    if rng.random() < 0.5:
        a = rng.choice([1, 2, 3])
        b = rng.choice([gx[-1] + 2, 14]) if rng.random() < 0.6 else gx[0] - 1
        if b > a + 2:
            s["lane"] = (a, min(b, 14), rng.choice([2, 3, 4]), rng.randrange(0, 20))
    pk = []
    for ch in chans:
        if not any(c == ch for _, c in plates) or rng.random() < 0.3:
            x = rng.choice([x for x in range(3, 14) if x not in gx])
            pk.append((x, rng.choice("ns"), ch))
    s["pockets"] = pk
    if rng.random() < 0.45:
        s["beam"] = (rng.choice([20, 30, 40]), rng.choice([0, 10, 20]), rng.randrange(0, 30))
    if rng.random() < 0.25:
        x = rng.choice([x for x in range(4, 14) if x not in gx])
        s["cross"] = [(x, rng.choice([20, 30]), rng.choice([10, 20]), rng.randrange(0, 30))]
    if rng.random() < 0.3:
        free = [x for x in range(4, 14) if x not in gx and all(x != p[0] for p in pk)]
        s["dial"] = rng.choice(free)
    s["exit"] = rng.choice(["e", "n", "s"])
    return s


def candidates():
    rng = random.Random(SEED)
    n = 0
    while n < N:
        sp = spec(rng)
        term = rng.choice([60, 80, 100, 120])
        loans = rng.choice([1, 1, 2])
        try:
            lv = build("6-x", f"g{n}", 6, sp, term=term, loans=loans)
        except Exception:
            continue
        n += 1
        yield f"g{n} term={term} loans={loans} {sp}", lv
