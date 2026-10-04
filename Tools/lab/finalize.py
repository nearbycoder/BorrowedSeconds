#!/usr/bin/env python3
"""Turns one sweep variant into a lab level file with its final identity.
    finalize.py DESIGN "LABEL PREFIX" cXY --id 6-3 --name Name --hint "..." [--debt] [--min-loans N] [--loans N]"""
import argparse, json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "designs"))
import importlib
ap = argparse.ArgumentParser()
ap.add_argument("design"); ap.add_argument("label"); ap.add_argument("out")
ap.add_argument("--id", required=True); ap.add_argument("--name", required=True); ap.add_argument("--hint", required=True)
ap.add_argument("--debt", action="store_true"); ap.add_argument("--min-loans", type=int, default=0)
ap.add_argument("--loans", type=int, default=None); ap.add_argument("--margin", type=int, default=3)
a = ap.parse_args()
mod = importlib.import_module(a.design)
for label, lv in mod.candidates():
    if label.strip().startswith(a.label.strip() + " ") or label.strip() == a.label.strip():
        break
else:
    sys.exit("no such label")
lv["id"], lv["name"], lv["hint"] = a.id, a.name, a.hint
lv["chapter"] = int(a.id.split("-")[0])
if a.loans is not None:
    lv["loans"] = a.loans
ex = {"borrow": True, "margin": a.margin, "startDelay": 20}
if a.debt: ex["debt"] = True
if a.min_loans: ex["minLoans"] = a.min_loans
lv["expect"] = ex
lv["notes"] = f"from {a.design}: {label[:300]}"
path = os.path.join(HERE, a.out + ".json")
json.dump(lv, open(path, "w"), indent=1)
print("wrote", path)
