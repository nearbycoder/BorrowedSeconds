#!/usr/bin/env python3
"""Level lab: run the solver on one level definition (JSON file or stdin) without touching
levels.json.  Usage:  python3 Tools/lab/lab.py level.json [--trace] [--quick]"""
import json, os, subprocess, sys, tempfile
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DOTNET = os.path.expanduser("~/Unity/Hub/Editor/6000.6.2f1/Editor/Data/DotNetSdk/dotnet")
DLL = os.path.join(ROOT, "Tools/Solver/bin/Release/net8.0/bs-solver.dll")

def run(level, trace=False, quick=False, margin_max=4):
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
        json.dump({"levels": [level]}, f)
        path = f.name
    args = [DOTNET, DLL, "--levels", path, "--no-write", "--margin-max", str(margin_max)]
    if trace: args += ["--trace", level["id"]]
    if quick: args += ["--quick"]
    out = subprocess.run(args, capture_output=True, text=True).stdout
    os.unlink(path)
    return out

if __name__ == "__main__":
    src = sys.argv[1]
    lv = json.load(open(src)) if src != "-" else json.load(sys.stdin)
    if "levels" in lv: lv = lv["levels"][0]
    print(run(lv, "--trace" in sys.argv, "--quick" in sys.argv))
