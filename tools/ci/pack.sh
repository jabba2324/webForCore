#!/usr/bin/env bash
# Packs webForCore's NuGet packages after a Release build: every project under src/ that WebFormsForCore.slnx builds,
# one at a time (packing the solution fails: its build dependencies on WebFormsForCore.Build make NuGet ask that multi-targeted
# project for GetTargetPath). Projects that aren't packable are skipped by NuGet.
# Usage: tools/ci/pack.sh <version> <output folder>
set -euo pipefail
cd "$(dirname "$0")/../.."
version=$1; out=$(realpath -m "$2")
mkdir -p "$out"
# The solution's projects under src/ that it builds (not <Build Project="false" />), without the build tools.
projects=$(python3 - <<'PY'
import xml.etree.ElementTree as ET
for p in ET.parse("WebFormsForCore.slnx").getroot().iter("Project"):
    path = p.get("Path", "")
    built = all(b.get("Project") != "false" for b in p.findall("Build"))
    if path.startswith("src/") and built and not path.startswith("src/WebFormsForCore.Build/"):
        print(path)
PY
)
projects=${projects//$'\r'/}  # Windows' Python ends its lines with CRLF (#46)
for project in $projects; do
  dotnet pack "$project" -c Release --no-build -nodeReuse:false -p:Version="$version" -o "$out" -v q -nologo
done
echo "$(ls "$out"/*.nupkg | wc -l) packages in $out"
