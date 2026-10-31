#!/usr/bin/env bash
# Builds and runs tools/ci/package-smoke, an application that uses webForCore through its packages only, against the
# packages in <package folder> (and nuget.org for everything else), then requests two pages.
# Usage: tools/ci/package-smoke.sh <package folder> <version>
set -euo pipefail
packages=$(cd "$1" && pwd); version=$2
work=$(mktemp -d)
trap 'kill $server 2>/dev/null || true; rm -rf "$work"' EXIT
cp -r "$(dirname "$0")/package-smoke/." "$work"
cd "$work"
cat > nuget.config <<CONFIG
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="webForCore" value="$packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="webForCore"><package pattern="webForCore" /><package pattern="webForCore.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
CONFIG
dotnet build Smoke.csproj -c Release -p:WebForCoreVersion="$version" -nodeReuse:false
dotnet bin_dotnet/Smoke.dll --urls http://127.0.0.1:5199 > server.log 2>&1 &
server=$!
for i in $(seq 120); do curl -s -o /dev/null http://127.0.0.1:5199/Default.aspx && break; sleep 1; done
fail=0
curl -s http://127.0.0.1:5199/Default.aspx | grep -q "webForCore package smoke test: 11/4.8" || { echo "Default.aspx: unexpected response"; fail=1; }
curl -s http://127.0.0.1:5199/Greet.aspx | grep -q "hello from App_Code" || { echo "Greet.aspx: unexpected response"; fail=1; }
# Markup.cs is compiled into the app against the packages' System.Web (issue #7: HttpUtility and IHtmlString, CS0433).
curl -s http://127.0.0.1:5199/Encode.aspx | grep -q "<b>&lt;compiled&gt;</b>" || { echo "Encode.aspx: unexpected response"; fail=1; }
if [ $fail -ne 0 ]; then cat server.log; exit 1; fi
echo "Package smoke test passed ($version)."
