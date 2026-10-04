from kit import *

# 5-2 Backswing: a rotor that meets crystal swings back. The block's chute is the last stretch
# to the exit; its top stop is at the edge of the hand's reach. Freeze it there: the block is
# out of the chute and the hand can no longer sweep the east side, so one loan clears both.
WANT = dict(borrow=True, margin=3, par=(100, 400))


def make(p):
    rows = [
        " ...     ",
        " .S....  ",
        " ...  .  ",
        "      .  ",
        "     ...  ",
        "      ..  ",
        "       .  ",
        "       .  ",
        "       .  ",
        "       .  ",
        "       .  ",
        "       X  ",
    ]
    lv = level("5-2", "Backswing", 5, rows,
               sliders=[slider([(7, 4), (7, 10)], speed=p["ss"], phase=p["sp"])],
               rotors=[rotor((5, 4), "N", 2, cw=True, turn=p["turn"], hold=p["hold"], phase=p["rp"])],
               term=100, loans=1,
               hint="A hand that meets crystal swings back the way it came.")
    return trim(transpose(lv))


def intended(tr, lv):
    b = tr.borrows()
    return len(b) == 1 and b[0][1] == "B0" and due_at_exit(tr, lv)


def candidates():
    for p in grid(ss=[2, 3], sp=[0, 5, 11], turn=[3, 4], hold=[6, 10, 16], rp=[0, 9]):
        yield label(p), make(p)
