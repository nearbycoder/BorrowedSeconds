#!/usr/bin/env python3
"""Parameter sweep for level design: runs the solver over many variants of one level idea in
parallel and keeps the variants that prove what the design needs.

    python3 Tools/lab/sweep.py Tools/lab/designs/d51.py [--jobs 6] [--budget 4000000] [--show 8]

A design file defines:
    def candidates():          yields (label, level_dict) pairs; level_dict is a lab-style level
    WANT = dict(borrow=True,   # unsolvable without loans          (optional, default True)
                debt=False,    # unsolvable with forgiven debts     (optional)
                margin=3,      # minimum hesitation margin in ticks (optional, default 3)
                par=(100, 500),# allowed par range in ticks         (optional)
                min_loans=0)   # prove fewer loans can't do it      (optional)
Passing variants are printed best first (higher margin, then par closest to the middle of the
range) and the best one is written next to the design as <design>.best.json.
"""
import concurrent.futures as cf
import importlib.util, json, os, re, subprocess, sys, tempfile, time

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DOTNET = os.path.expanduser("~/Unity/Hub/Editor/6000.6.2f1/Editor/Data/DotNetSdk/dotnet")
DLL = os.path.join(ROOT, "Tools/Solver/bin/Release/net8.0/bs-solver.dll")


def load(path):
    sys.path.insert(0, os.path.dirname(os.path.abspath(path)))
    spec = importlib.util.spec_from_file_location("design", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def run_one(level, budget, margin_max, timeout):
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
        json.dump({"levels": [level]}, f)
        path = f.name
    args = [DOTNET, DLL, "--levels", path, "--no-write", "--margin-max", str(margin_max), "--max-states", str(budget)]
    t0 = time.time()
    try:
        r = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
        out = r.stdout + ("\nERROR " + r.stderr.strip().splitlines()[0] if r.returncode and r.stderr.strip() else "")
    except subprocess.TimeoutExpired:
        out = "TIMEOUT"
    finally:
        os.unlink(path)
    return parse(out), time.time() - t0, out


LINE = re.compile(r"(\d+\.\d)s\s+(\d+\.\d+)s\s+(\d+)\s+(.+?)\s+(proved|SOLVABLE|limit\?|-)\s+(-|\d+)\s+(OK|FAIL)\s*$")


def parse(out):
    """Pulls par/loans/proofs/margin out of the solver's table line."""
    for line in out.splitlines():
        m = LINE.search(line)
        if not m:
            continue
        nb = m.group(4).split()
        return {
            "solved": True,
            "par": round(float(m.group(2)) * 20),
            "loans": int(m.group(3)),
            "noBorrow": nb[0],
            "minLoans": nb[1] if len(nb) > 1 else "",
            "forgiven": m.group(5),
            "margin": int(m.group(6)) if m.group(6) != "-" else -1,
            "line": line,
        }
    return {"solved": False, "line": out.strip()[-160:]}


def trace(level, budget):
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
        json.dump({"levels": [level]}, f)
        path = f.name
    try:
        return subprocess.run([DOTNET, DLL, "--levels", path, "--trace", level["id"], "--max-states", str(budget)],
                              capture_output=True, text=True, timeout=600).stdout
    finally:
        os.unlink(path)


class Trace:
    """A parsed solver trace: action lines plus the boards rendered after borrows, dues and thaws."""
    def __init__(self, text):
        self.text = text
        self.events = []   # (tick, action, (px,py), line, board rows or None)
        lines = text.splitlines()
        i = 0
        while i < len(lines):
            m = re.match(r"t=\s*(\d+) \(.*?\) (\S+)\s+P=\((\d+),(\d+)\)", lines[i])
            if m:
                board = []
                j = i + 1
                while j < len(lines) and lines[j].startswith("   ") and not lines[j].startswith("t="):
                    board.append(lines[j][3:])
                    j += 1
                self.events.append((int(m.group(1)), m.group(2), (int(m.group(3)), int(m.group(4))), lines[i], board or None))
                i = j
            else:
                i += 1

    def borrows(self):
        return [e for e in self.events if e[1].startswith("B")]

    @staticmethod
    def find(board, chars):
        return [(x, y) for y, row in enumerate(board or []) for x, c in enumerate(row) if c in chars]


def judge(res, want):
    if not res.get("solved"):
        if "ERROR" in res.get("line", ""):
            return False, "error=" + res["line"].split("ERROR", 1)[1][:80]
        return False, "unsolved"
    if want.get("borrow", True) and res["noBorrow"] != "proved":
        return False, f"noBorrow={res['noBorrow']}"
    if want.get("debt") and res["forgiven"] != "proved":
        return False, f"forgiven={res['forgiven']}"
    if res["margin"] < want.get("margin", 3):
        return False, f"margin={res['margin']}"
    lo, hi = want.get("par", (0, 10_000))
    if not lo <= res["par"] <= hi:
        return False, f"par={res['par']}"
    if want.get("min_loans", 0) > 1 and not res["minLoans"].endswith(":proved"):
        return False, "minLoans"
    return True, "ok"


def main():
    args = sys.argv[1:]
    design = args[0]
    jobs = int(args[args.index("--jobs") + 1]) if "--jobs" in args else 6
    budget = int(args[args.index("--budget") + 1]) if "--budget" in args else 4_000_000
    show = int(args[args.index("--show") + 1]) if "--show" in args else 8
    timeout = int(args[args.index("--timeout") + 1]) if "--timeout" in args else 240
    verbose = "-v" in args
    mod = load(design)
    want = dict(borrow=True, margin=3)
    want.update(getattr(mod, "WANT", {}))
    cands = list(mod.candidates())
    for label, lv in cands:
        ex = lv.setdefault("expect", {})
        ex["borrow"] = want.get("borrow", True)
        ex["debt"] = bool(want.get("debt", False))
        ex["margin"] = want.get("margin", 3)
        ex["startDelay"] = want.get("start_delay", 20)
        if want.get("min_loans", 0) > 1: ex["minLoans"] = want["min_loans"]
    print(f"{len(cands)} candidates, {jobs} jobs, budget {budget:,}", flush=True)
    passed, reasons = [], {}
    t0 = time.time()
    with cf.ThreadPoolExecutor(jobs) as pool:
        futs = {pool.submit(run_one, lv, budget, max(3, want.get("margin", 3)), timeout): (label, lv) for label, lv in cands}
        for fut in cf.as_completed(futs):
            label, lv = futs[fut]
            res, secs, raw = fut.result()
            ok, why = judge(res, want)
            reasons[why.split("=")[0]] = reasons.get(why.split("=")[0], 0) + 1
            if verbose or ok:
                print(f"{'PASS' if ok else 'fail'} {label:<40} {why:<16} {secs:5.1f}s  {res.get('line', '')[22:]}", flush=True)
            if ok and hasattr(mod, "intended"):
                tr = Trace(trace(lv, budget))
                import inspect
                ok = bool(mod.intended(tr, lv) if len(inspect.signature(mod.intended).parameters) > 1 else mod.intended(tr))
                if not ok:
                    reasons["intent"] = reasons.get("intent", 0) + 1
                    print(f"  (unintended) {label}", flush=True)
            if ok:
                passed.append((label, lv, res))
    print(f"done in {time.time() - t0:.0f}s: {len(passed)} pass; " + ", ".join(f"{k} {v}" for k, v in sorted(reasons.items())))
    if not passed:
        return 1
    lo, hi = want.get("par", (0, 10_000))
    mid = (lo + min(hi, 600)) / 2
    passed.sort(key=lambda p: (-p[2]["margin"], abs(p[2]["par"] - mid)))
    for label, lv, res in passed[:show]:
        print(f"  {label:<40} par {res['par'] / 20:5.2f}s margin {res['margin']} loans {res['loans']}")
    best = passed[0][1]
    out = os.path.splitext(design)[0] + ".best.json"
    json.dump(best, open(out, "w"), indent=1)
    print("best ->", out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
