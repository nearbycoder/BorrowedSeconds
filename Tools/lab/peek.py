#!/usr/bin/env python3
"""Shows passing variants of a sweep: map, solver line and the key trace events.
    peek.py d62 [max] [filter-substring]"""
import json, os, re, subprocess, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "designs"))
import importlib
from sweep import trace, Trace, run_one
name = sys.argv[1]
limit = int(sys.argv[2]) if len(sys.argv) > 2 else 6
filt = sys.argv[3] if len(sys.argv) > 3 else ""
mod = importlib.import_module(name)
log = open(f"/tmp/sw_{name}.txt").read().splitlines()
passing = []
for l in log:
    if l.startswith("PASS "):
        passing.append(l[5:].split(" ok ", 1)[0].strip())
unint = {l.split("(unintended) ", 1)[1].strip() for l in log if "(unintended)" in l}
want = [p for p in passing if p not in unint and filt in p]
cands = dict((lab.strip(), lv) for lab, lv in mod.candidates())
for lab in want[:limit]:
    lv = cands.get(lab)
    if lv is None:
        continue
    print("=" * 70)
    print(lab[:160])
    print("\n".join("   |" + r + "|" for r in lv["map"]))
    tr = Trace(trace(lv, 4_000_000))
    for e in tr.events:
        line = e[3]
        if re.search(r"\bB\d|Due|Thaw\(|Latch|GateOpen|GateClose|Win", line):
            print("   " + line[:110])
