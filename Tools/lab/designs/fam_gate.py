"""Family generator for plate/gate puzzles on one lane.

spec keys:
    gates:   list of (x, channel)                       gates on the lane (row LANE)
    ring:    (x0, side, speed, phase, dir, plates)      loop slider around a 2x1 block north
             ("n") or south ("s") of the lane; plates = list of (ring_index, channel)
    lane:    (x0, x1, speed, phase)                     ping-pong slider on the lane between x0..x1
    beam:    (on, off, phase)                           laser on the west wall firing along the lane
    cross:   list of (x, on, off, phase)                lasers firing south across the lane
    pockets: list of (x, side, channel)                 plate in a one-tile pocket off the lane
    dial:    x or None                                  time-lock on the lane
    exit:    "e" | "n" | "s"
"""
from kit import level, slider, laser, rotor, trim

H = 11
LANE = 5
W = 16


def ring_tiles(x0, y0):
    """Clockwise 10-tile ring around a 2x1 block whose top-left ring tile is (x0, y0)."""
    top = [(x0 + i, y0) for i in range(4)]
    right = [(x0 + 3, y0 + 1)]
    bottom = [(x0 + 3 - i, y0 + 2) for i in range(4)]
    left = [(x0, y0 + 1)]
    return top + right + bottom + left


def build(id, name, chapter, spec, term=100, loans=None, hint=""):
    rows = [[" "] * W for _ in range(H)]
    for x in range(1, W - 1):
        rows[LANE][x] = "."
    for y in range(LANE - 3, LANE):
        for x in range(0, 3):
            rows[y][x] = "."
    rows[LANE - 2][1] = "S"
    sliders, lasers = [], []
    for x, ch in spec.get("gates", []):
        rows[LANE][x] = ch.upper()
    ring = spec.get("ring")
    if ring:
        x0, side, speed, ph, d, plates = ring
        y0 = LANE - 4 if side == "n" else LANE + 2
        tiles = ring_tiles(x0, y0)
        for (x, y) in tiles:
            rows[y][x] = "."
        rows[y0 + 1][x0 + 1] = "#"
        rows[y0 + 1][x0 + 2] = "#"
        for idx, ch in plates:
            x, y = tiles[idx % len(tiles)]
            rows[y][x] = ch
        corners = [tiles[0], tiles[3], tiles[5], tiles[8]]
        sliders.append(slider(corners, loop=True, speed=speed, phase=ph, dir=d))
    lane = spec.get("lane")
    if lane:
        a, b, speed, ph = lane
        sliders.append(slider([(a, LANE), (b, LANE)], speed=speed, phase=ph))
    for x, side, ch in spec.get("pockets", []):
        y = LANE - 1 if side == "n" else LANE + 1
        rows[y][x] = ch
    if spec.get("dial") is not None:
        rows[LANE][spec["dial"]] = "L"
    beam = spec.get("beam")
    if beam:
        rows[LANE][0] = "#"
        lasers.append(laser((0, LANE), "E", on=beam[0], off=beam[1], phase=beam[2]))
    for x, on, off, ph in spec.get("cross", []):
        rows[LANE - 2][x] = "#"
        rows[LANE + 2][x] = "#"
        lasers.append(laser((x, LANE - 2), "S", on=on, off=off, phase=ph))
    ex = spec.get("exit", "e")
    if ex == "e":
        rows[LANE][W - 1] = "X"
    elif ex == "n":
        rows[LANE - 1][W - 2] = "X"
    else:
        rows[LANE + 1][W - 2] = "X"
    rows = ["".join(r) for r in rows]
    return trim(level(id, name, chapter, rows, sliders=sliders, lasers=lasers, term=term, loans=loans, hint=hint))
