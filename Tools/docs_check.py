#!/usr/bin/env python3
"""Cross-checks the level claims in README.md and docs/PLAN.md against the shipped data:
levels.json (names, chapters, what validate.sh is asked to prove) and solutions.json (par,
margin). Prints every mismatch and exits non-zero if there is one.   python3 Tools/docs_check.py"""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LEVELS = json.load(open(os.path.join(ROOT, "Assets/Resources/Levels/levels.json")))["levels"]
SOLS = {s["id"]: s for s in json.load(open(os.path.join(ROOT, "Assets/Resources/Levels/solutions.json")))["levels"]}
README = open(os.path.join(ROOT, "README.md")).read()
PLAN = open(os.path.join(ROOT, "docs/PLAN.md")).read()
BY_ID = {l["id"]: l for l in LEVELS}
WORDS = "zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen " \
        "seventeen eighteen nineteen twenty".split()
TENS = {"twenty": 20, "thirty": 30, "forty": 40, "fifty": 50, "sixty": 60}
bad = []


def number_word(n):
    if n < 20:
        return WORDS[n]
    t = next(k for k, v in TENS.items() if v == n // 10 * 10)
    return t + ("-" + WORDS[n % 10] if n % 10 else "")


def proofs(l):
    """The claims validate.sh checks for a level, in the README's notation."""
    e = l.get("expect", {})
    out = []
    if e.get("borrow"): out.append("B")
    if e.get("debt"): out.append("D")
    if e.get("minLoans"): out.append(f"needs ≥ {e['minLoans']} loans")
    return out


def table_rows(text, header_start):
    """Rows of every markdown table whose header starts with header_start."""
    rows, on = [], False
    for line in text.splitlines():
        if line.startswith(header_start):
            on = True
            continue
        if on and line.startswith("|---"):
            continue
        if on and line.startswith("|"):
            rows.append([c.strip() for c in line.strip().strip("|").split("|")])
        else:
            on = False
    return rows


# README: the per-level table
seen = set()
for r in table_rows(README, "| # | Name | Obstacles and devices | Proven |"):
    id, name, _, proven = r[:4]
    seen.add(id)
    l = BY_ID.get(id)
    if not l:
        bad.append(f"README lists {id} {name}, which isn't in levels.json")
        continue
    if l["name"] != name:
        bad.append(f"README names {id} '{name}', levels.json says '{l['name']}'")
    claim = [p.strip().replace(">=", "≥") for p in proven.split(",")]
    claim = ["needs ≥ " + p.split("≥")[1].strip() if "≥" in p else p for p in claim]
    if claim != proofs(l):
        bad.append(f"README claims {id} proves {claim}, levels.json asks validate.sh for {proofs(l)}")
for l in LEVELS:
    if l["id"] not in seen:
        bad.append(f"README's level table is missing {l['id']} {l['name']}")

# README: the chapter table lists each chapter's levels in order
chapters = {}
for l in LEVELS:
    chapters.setdefault(l["chapter"], []).append(l["name"])
roman = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII"]
rows = table_rows(README, "| Chapter | Introduces | Levels |")
if len(rows) != len(chapters):
    bad.append(f"README's chapter table has {len(rows)} rows, levels.json has {len(chapters)} chapters")
for r in rows:
    num = roman.index(r[0].split("·")[0].strip())
    names = [n.strip() for n in r[2].split(",")]
    if names != chapters.get(num):
        bad.append(f"README chapter {r[0]} lists {names}, levels.json has {chapters.get(num)}")

# README: counts in prose and the badge
n = len(LEVELS)
debt = sum(1 for l in LEVELS if l.get("expect", {}).get("debt"))
for pat, want in [(r"levels-(\d+)%2C", n), (r"every one of the (\d+) levels", n), (r"autoplays all (\d+) levels", n),
                  (r"at par on all (\d+) levels", n)]:
    for m in re.finditer(pat, README):
        if int(m.group(1)) != want:
            bad.append(f"README says '{m.group(0)}', there are {want}")
m = re.search(r"(\w[\w-]*) single-screen levels in (\w+) chapters", README)
if not m or m.group(1).lower() != number_word(n) or m.group(2).lower() != number_word(len(chapters)):
    bad.append(f"README's content line reads '{m.group(0) if m else '?'}', expected {number_word(n)} levels in {number_word(len(chapters))} chapters")
m = re.search(r"(\w[\w-]*) levels are proven to need \*the debt itself\*", README)
if not m or m.group(1).lower() != number_word(debt):
    bad.append(f"README's debt count reads '{m.group(1) if m else '?'}', {debt} levels claim D")

readme_rows = len(seen)
# PLAN.md section 6: id, name, proof, par and margin per level
seen = set()
for line in PLAN.splitlines():
    m = re.match(r"\| (\d+-\d) \| ([^|]+?) \| [^|]+ \| [^|]+ \| ([^|]+?) \| (\d+\.\d+) s \| (\d+) \|$", line)
    if not m:
        continue
    id, name, proof, par, margin = m.groups()
    seen.add(id)
    l, s = BY_ID.get(id), SOLS.get(id)
    if not l or not s:
        bad.append(f"PLAN lists {id}, which isn't shipped")
        continue
    if l["name"] != name:
        bad.append(f"PLAN names {id} '{name}', levels.json says '{l['name']}'")
    if abs(float(par) * 20 - s["par"]) > 0.01:
        bad.append(f"PLAN gives {id} par {par} s, solutions.json says {s['par'] / 20:.2f} s")
    if int(margin) != s["margin"]:
        bad.append(f"PLAN gives {id} margin {margin}, solutions.json says {s['margin']}")
    want = proofs(l)
    got = [p.strip() for p in proof.replace("≥2", "≥ 2").split(",")]
    got = ["needs ≥ " + p.split("≥")[1].strip() + " loans" if "≥" in p else p for p in got]
    got = [p.replace(" loans loans", " loans") for p in got]
    if got != want:
        bad.append(f"PLAN claims {id} proves {got}, levels.json asks for {want}")
for l in LEVELS:
    if l["id"] not in seen:
        bad.append(f"PLAN section 6 is missing {l['id']} {l['name']}")

print("\n".join(bad) if bad else f"docs match the data: {n} levels, {len(chapters)} chapters, {debt} need the debt "
      f"(README rows {readme_rows}, chapter rows {len(rows)}, PLAN rows {len(seen)})")
sys.exit(1 if bad else 0)
