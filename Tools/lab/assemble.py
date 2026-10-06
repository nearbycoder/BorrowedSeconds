#!/usr/bin/env python3
"""Writes Assets/Resources/Levels/levels.json from the lab files in order, with one map row per line."""
import json, os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
LAB = os.path.join(ROOT, "Tools/lab")
import re
# every cCL.json in Tools/lab, ordered by chapter C (one or more digits) then level L (last digit)
_files = [f[:-5] for f in os.listdir(LAB) if re.fullmatch(r"c\d{2,3}\.json", f)]
ORDER = sorted(_files, key=lambda n: (int(n[1:-1]), int(n[-1])))
# only whole chapters ship: a chapter still being designed (fewer than five files) is left out
_counts = {}
for n in ORDER:
    _counts[n[1:-1]] = _counts.get(n[1:-1], 0) + 1
_partial = sorted({n[1:-1] for n in ORDER if _counts[n[1:-1]] < 5}, key=int)
if _partial:
    print("skipping unfinished chapters:", ", ".join(_partial), file=sys.stderr)
ORDER = [n for n in ORDER if _counts[n[1:-1]] >= 5]
KEYS = ["id","name","chapter","term","loans","hint","spoiler","map","sliders","lasers","rotors","expect","notes"]

def fmt(level):
    lines = ["    {"]
    items = [k for k in KEYS if k in level] + [k for k in level if k not in KEYS]
    for i, k in enumerate(items):
        v = level[k]
        comma = "," if i < len(items) - 1 else ""
        if k == "map":
            lines.append('      "map": [')
            for j, row in enumerate(v):
                lines.append('        ' + json.dumps(row) + ("," if j < len(v) - 1 else ""))
            lines.append("      ]" + comma)
        elif isinstance(v, list):
            lines.append(f'      {json.dumps(k)}: [')
            for j, o in enumerate(v):
                lines.append('        ' + json.dumps(o) + ("," if j < len(v) - 1 else ""))
            lines.append("      ]" + comma)
        else:
            lines.append(f'      {json.dumps(k)}: {json.dumps(v)}{comma}')
    lines.append("    }")
    return "\n".join(lines)

levels = [json.load(open(os.path.join(LAB, n + ".json"))) for n in ORDER]
out = '{\n  "levels": [\n' + ",\n".join(fmt(l) for l in levels) + "\n  ]\n}\n"
open(os.path.join(ROOT, "Assets/Resources/Levels/levels.json"), "w").write(out)
print("wrote", len(levels), "levels:", ", ".join(l["id"] + " " + l["name"] for l in levels))
