#!/usr/bin/env python3
"""Writes one variant of a design to a lab file:  pick.py d61 "<exact label>" c61"""
import json, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "designs"))
import importlib
mod = importlib.import_module(sys.argv[1])
want = sys.argv[2].strip()
for label, lv in mod.candidates():
    if label.strip() == want:
        out = os.path.join(os.path.dirname(os.path.abspath(__file__)), sys.argv[3] + ".json")
        json.dump(lv, open(out, "w"), indent=1)
        print("wrote", out)
        break
else:
    sys.exit("no such label")
