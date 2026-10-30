#!/usr/bin/env bash
# Phase 2 compile spike: build the fork's System.Web for net11.0, then try to compile each
# ASP.NET Web Stack project against it and group the compile errors by missing API.
#
# Usage: tools/lift/spike.sh <WebStackRoot> <ForkRoot>
#   WebStackRoot  checkout of https://github.com/aspnet/AspNetWebStack
#   ForkRoot      checkout of the webForCore fork
# Run it from a directory OUTSIDE the fork (output goes to ./lift-spike), so the fork's
# Directory.Build.props does not apply to the generated projects.
# Needs: .NET SDK 11, python3, network access to NuGet. On Fedora/RHEL also
# OPENSSL_ENABLE_SHA1_SIGNATURES=1 (strong-name signing uses SHA-1).
set -uo pipefail

WEBSTACK="${1:?usage: spike.sh <WebStackRoot> <ForkRoot>}"
FORK="${2:?usage: spike.sh <WebStackRoot> <ForkRoot>}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$(pwd)/lift-spike"
GEN="$OUT/generated"
LOGS="$OUT/build-logs"
WEBSTACK="$(cd "$WEBSTACK" && pwd)"
FORK="$(cd "$FORK" && pwd)"

# Dependency order: libraries with no System.Web dependency first.
PROJECTS=(System.Web.Razor System.Web.Http System.Web.Cors System.Web.Http.Cors System.Web.Http.Owin
          System.Web.WebPages.Deployment System.Web.Helpers System.Web.WebPages System.Web.WebPages.Razor System.Web.Http.WebHost
          System.Web.Mvc Microsoft.Web.Mvc)

command -v dotnet >/dev/null || { echo "dotnet SDK not found"; exit 1; }
dotnet --version | grep -qE '^1[1-9]\.' || { echo "Need .NET SDK 11 or newer, found $(dotnet --version)"; exit 1; }
mkdir -p "$GEN" "$LOGS"

echo "== Step 1: build the fork's System.Web for net11.0 (baseline)"
dotnet build "$FORK/src/WebFormsForCore.Web/WebFormsForCore.Web.csproj" -c Release -f net11.0 -nologo \
  > "$LOGS/00-fork-baseline.log" 2>&1
if [ $? -ne 0 ]; then
  echo "Baseline build of the fork FAILED. See $LOGS/00-fork-baseline.log. Fix this before continuing."
  python3 "$HERE/group_errors.py" "$LOGS/00-fork-baseline.log" | head -40
  exit 2
fi
echo "   baseline OK"

echo "== Step 2: generate SDK-style net11.0 projects from the Web Stack csproj files"
cat > "$GEN/Directory.Build.props" <<PROPS
<Project>
  <PropertyGroup>
    <WfcRoot>$FORK</WfcRoot>
    <WebStackRoot>$WEBSTACK</WebStackRoot>
  </PropertyGroup>
</Project>
PROPS
# The shared framework ships a System.Web.dll facade (HttpUtility forwards). Next to the fork's System.Web
# it makes the compiler see two System.Web assemblies (CS7069), so drop the facade, as the fork's projects do.
cat > "$GEN/Directory.Build.targets" <<'TARGETS'
<Project>
  <Target Name="RemoveFrameworkSystemWebFacade" BeforeTargets="FindReferenceAssembliesForReferences;ResolveReferences" AfterTargets="ResolveAssemblyReferences">
    <ItemGroup>
      <ReferencePath Remove="%(Identity)" Condition="'%(FileName)' == 'System.Web' AND $([System.Text.RegularExpressions.Regex]::IsMatch(%(Identity),'[\\/]packs[\\/]Microsoft\.NETCore\.App\.Ref[\\/]'))" />
      <!-- The fork ships its own System.Configuration.ConfigurationManager; drop the NuGet one that arrives transitively. -->
      <ReferencePath Remove="%(Identity)" Condition="'%(FileName)' == 'System.Configuration.ConfigurationManager' AND $([System.Text.RegularExpressions.Regex]::IsMatch(%(Identity),'(?i)[\\/]system\.configuration\.configurationmanager[\\/]'))" />
    </ItemGroup>
  </Target>
</Project>
TARGETS
python3 "$HERE/gen_csproj.py" "$WEBSTACK" "$GEN" "${PROJECTS[@]}" | tee "$OUT/generator-report.txt"

echo "== Step 3: build each project (errors are expected; we are measuring them)"
i=0
for p in "${PROJECTS[@]}"; do
  i=$((i+1)); log="$LOGS/$(printf '%02d' $i)-$p.log"
  dotnet build "$GEN/$p/$p.csproj" -c Release -nologo -v:minimal > "$log" 2>&1
  status=$?
  errs=$(grep -E ': error [A-Z]+[0-9]+:' "$log" | sed -E 's/ \[[^]]*\]$//' | sort -u | wc -l)
  printf '   %-34s exit=%s errors=%s\n' "$p" "$status" "$errs"
done

echo "== Step 4: group errors"
python3 "$HERE/group_errors.py" "$LOGS"/[0-9][0-9]-*.log > "$OUT/REPORT.md"
head -30 "$OUT/REPORT.md"
echo
echo "Full report: $OUT/REPORT.md   Logs: $LOGS   Generator notes: $OUT/generator-report.txt"
