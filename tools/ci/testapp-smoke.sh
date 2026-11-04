#!/usr/bin/env bash
# Rebuilds src/WebFormsForCore.TestApp after a build of WebFormsForCore.slnx (Release) with its pages newer than their
# designer files, so that the designer-file task runs, then starts it and requests its pages (#80).
# Usage: tools/ci/testapp-smoke.sh
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
app=$root/src/WebFormsForCore.TestApp
port=5197
log=$(mktemp)
cleanup() { kill $server 2>/dev/null || true; rm -f "$log"; }
server=
trap cleanup EXIT

# Pages newer than their designer files make CreateAspDesignerFiles regenerate them after the build. Upstream's
# WebFormsForCore.Build package started "dotnet.exe" for that, which doesn't exist on Linux: the build failed after any
# edit of a page, and a fresh checkout (CI) never ran it. The TestApp now uses this repository's tasks.
find "$app" -maxdepth 1 \( -name '*.aspx' -o -name '*.ascx' -o -name '*.master' -o -name '*.Master' \) -exec touch {} +
dotnet build "$app/WebFormsForCore.TestApp.csproj" -c Release -f net10.0 -p:BuildProjectReferences=false -nodeReuse:false

fail=0
# deps.json is the SDK's, with the real assemblyVersion of System.Web (webForCore.WebStack.targets), so the shared
# framework's System.Web facade (4.0.0.0) doesn't win at startup. The TestApp used to copy a hand-maintained deps.json
# (written for .NET 8) over it; without the version the app can't load System.Web 11.0.0.0 and doesn't start.
deps=$app/bin_dotnet/WebFormsForCore.TestApp.deps.json
grep -q '"System.Web.dll": { "assemblyVersion": "11.0.0.0"' "$deps" || { echo "deps.json: no assemblyVersion for System.Web"; fail=1; }
grep -q '".NETCoreApp,Version=v10.0"' "$deps" || { echo "deps.json: not the SDK's for net10.0"; fail=1; }
[ -z "$(cd "$app" && git ls-files '*.deps.json')" ] || { echo "checked-in deps.json files: $(cd "$app" && git ls-files '*.deps.json')"; fail=1; }

# The TestApp's own Kestrel endpoint (appsettings.json) is localhost:5000; configuration overrides it.
(cd "$app" && Kestrel__Endpoints__Http__Url=http://127.0.0.1:$port exec dotnet bin_dotnet/WebFormsForCore.TestApp.dll) > "$log" 2>&1 &
server=$!
for i in $(seq 120); do curl -s -o /dev/null http://127.0.0.1:$port/favicon.ico && break; sleep 1; done
# FriendlyUrls redirects /Default.aspx to /Default (301); -L follows it.
for page in / /Default.aspx /About /Contact /Test /SessionTest; do
  body=$(curl -sL -w '\n%{http_code}' http://127.0.0.1:$port$page)
  code=${body##*$'\n'}
  if [ "$code" != 200 ] || ! grep -q '__VIEWSTATE' <<< "$body"; then echo "$page: $code"; fail=1; fi
done
code=$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$port/NoSuchPage.aspx)
[ "$code" = 404 ] || { echo "/NoSuchPage.aspx: $code (expected 404)"; fail=1; }
if [ $fail -ne 0 ]; then cat "$log"; exit 1; fi
echo "TestApp smoke test passed."
