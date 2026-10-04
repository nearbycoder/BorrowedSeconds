#!/usr/bin/env bash
# Builds and runs the console solver against Assets/Resources/Levels/levels.json.
#   Tools/validate.sh                 prove every level, write solutions.json
#   Tools/validate.sh --level 1-5     one level
#   Tools/validate.sh --quick         solve only (skip proofs and margins)
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-$HOME/Unity/Hub/Editor/6000.6.2f1/Editor/Data/DotNetSdk/dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
"$DOTNET" build "$ROOT/Tools/Solver/Solver.csproj" -c Release -v quiet -nologo -clp:ErrorsOnly >&2
exec "$DOTNET" "$ROOT/Tools/Solver/bin/Release/net8.0/bs-solver.dll" \
  --levels "$ROOT/Assets/Resources/Levels/levels.json" \
  --out "$ROOT/Assets/Resources/Levels/solutions.json" "$@"
