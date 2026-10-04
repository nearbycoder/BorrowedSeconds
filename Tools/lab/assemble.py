#!/usr/bin/env python3
"""Writes Assets/Resources/Levels/levels.json from the lab files in order, with one map row per line."""
import json, os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
LAB = os.path.join(ROOT, "Tools/lab")
ORDER = ["c11","c12","c13","c14","c15","c21","c22","c23","c24","c25","c31","c32","c33","c34","c35","c41","c42","c43","c44","c45"]
KEYS = ["id","name","chapter","term","loans","hint","map","sliders","lasers","rotors","expect","notes"]

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
