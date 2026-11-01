#!/usr/bin/env bash
# Builds and runs tools/ci/package-smoke, an application that uses webForCore through its packages only, against the
# packages in <package folder> (and nuget.org for everything else), then requests its pages and checks where the W3C
# log went.
# Usage: tools/ci/package-smoke.sh <package folder> <version>
set -euo pipefail
# pwd -W: the Windows form of the path under Git Bash (#46), since it is written into nuget.config.
packages=$(cd "$1" && { pwd -W 2>/dev/null || pwd; }); version=$2
here=$(cd "$(dirname "$0")" && pwd)
base=$(mktemp -d); work=$base/site; mkdir "$work"
trap 'kill $server 2>/dev/null || true; rm -rf "$base"' EXIT
cp -r "$here/package-smoke/." "$work"
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
# Service.cs, compiled into the app, names System.Web.Services.Description types, which the System.Web.Services.Description
# package that CoreWCF brings defines too (issue #34: CS0433, so the build itself failed); Ping.ashx serves it.
curl -s http://127.0.0.1:5199/Ping.ashx | grep -q "ping Literal" || { echo "Ping.ashx: unexpected response"; fail=1; }
# A Repeater with ItemType needs System.Web.DynamicData, which the webForCore package brings (issue #8).
curl -s http://127.0.0.1:5199/Typed.aspx | grep -q "\[typed\]\[repeater\]" || { echo "Typed.aspx: unexpected response"; fail=1; }
# With relaxedUrlToFileSystemMapping="true" (Web.config), MapPath still maps (issue #27: every MapPath returned
# NOT_A_VALID_FILESYSTEM_PATH on Linux).
curl -s http://127.0.0.1:5199/MapPath.aspx | grep -q "mapped: True" || { echo "MapPath.aspx: unexpected response"; curl -s http://127.0.0.1:5199/MapPath.aspx | head -5; fail=1; }
# The W3C log (AddWebForms' defaults) is next to the site, in logs/<site>/W3SVC1, never in it (issue #15: the site
# served it at /logs/W3SVC1/u_ex<date>.0000.txt). The logger flushes every second.
sleep 2
ls "$base"/logs/site/W3SVC1/u_ex*.txt > /dev/null 2>&1 || { echo "W3C log: not in logs/site/W3SVC1 next to the site"; fail=1; }
[ ! -e "$work/logs" ] || { echo "W3C log: written in the site ($work/logs)"; fail=1; }
log=$(ls "$base"/logs/site/W3SVC1/ 2>/dev/null | head -1 || true)
[ "$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:5199/logs/W3SVC1/${log:-u_ex.txt}")" = 404 ] || { echo "W3C log: served"; fail=1; }
if [ $fail -ne 0 ]; then cat server.log; exit 1; fi
kill $server; wait $server 2>/dev/null || true

# tools/ci/package-smoke-mvc: MVC 5 and OWIN with the third-party packages applications have, as they have them.
mvc=$base/mvc; mkdir "$mvc"
cp -r "$here/package-smoke-mvc/." "$mvc"
cp nuget.config "$mvc"
cd "$mvc"
dotnet build SmokeMvc.csproj -c Release -p:WebForCoreVersion="$version" -nodeReuse:false
# The output has webForCore's MVC, Razor, Web Pages and Helpers, not the .NET Framework ones that Unity.Mvc brings
# (issue #23: Microsoft.AspNet.Razor and WebPages won the SDK's conflict resolution on file version; issue #28: with no
# webForCore.Web.Helpers in the build, Microsoft.AspNet.WebPages' System.Web.Helpers was the only one, and an MVC app
# without a package that brings it couldn't load the System.Web.Helpers its web.config lists).
if grep -q -E '"lib/net4[0-9]*/System\.Web\.(Mvc|Razor|WebPages[A-Za-z.]*|Helpers)\.dll"' bin_dotnet/SmokeMvc.deps.json; then
  echo "deps.json: .NET Framework assemblies replaced webForCore's:"
  grep -o -E '"lib/net4[0-9]*/System\.Web\.(Mvc|Razor|WebPages[A-Za-z.]*|Helpers)\.dll"' bin_dotnet/SmokeMvc.deps.json | sort -u
  fail=1
fi
dotnet bin_dotnet/SmokeMvc.dll --urls http://127.0.0.1:5198 > server.log 2>&1 &
server=$!
for i in $(seq 120); do curl -s -o /dev/null http://127.0.0.1:5198/ && break; sleep 1; done
# A Razor view renders although Unity.Mvc brings Microsoft.AspNet.Razor 3.2.7 (issue #23: it replaced webForCore's in
# the output, and every view failed with a MissingMethodException).
curl -s http://127.0.0.1:5198/Plain | grep -q "plain view, rendered by Razor" || { echo "/Plain: unexpected response"; curl -s http://127.0.0.1:5198/Plain | grep -i -m2 '<title>'; fail=1; }
# System.Web.Helpers, listed in Web.config and used by the view, comes with webForCore.Web.Mvc, as with
# Microsoft.AspNet.Mvc (issue #28: Configuration Error, "Could not load file or assembly 'System.Web.Helpers'").
curl -s http://127.0.0.1:5198/Plain | grep -q '{"helpers":true}' || { echo "/Plain: System.Web.Helpers missing"; fail=1; }
# webForCore's SignalR and DataVisualization have Microsoft's assembly versions, so Microsoft's SignalR SQL Server
# scale-out (built against SignalR 2.4.3) compiles and binds (issue #40: CS1705 against SignalR.Core 0.0.0.0, JabbR).
curl -s http://127.0.0.1:5198/Versions | grep -q "SignalR 2.4.3.0, scale-out 2.4.3.0, DataVisualization 4.0.0.0" || { echo "/Versions: unexpected response"; curl -s http://127.0.0.1:5198/Versions | head -c 300; echo; fail=1; }
# Unity, started from Application_Start (WebActivatorEx's start methods don't run on webForCore, issue #21; the original
# package is in the build all the same and must not break it), constructs the controller, and its Razor view renders
# although Unity.Mvc brings Microsoft.AspNet.Razor 3.2.7 (issue #23: it replaced webForCore's, MissingMethodException).
home=$(curl -s http://127.0.0.1:5198/)
echo "$home" | grep -q "hello from Unity, rendered by Razor" || { echo "/: unexpected response"; echo "$home" | head -20; fail=1; }
# Katana 3.0.1's Google provider redirects a challenge to Google (issue #18: it needs System.Net.Http.WebRequest, and
# every request failed).
location=$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' http://127.0.0.1:5198/challenge)
case "$location" in "302 https://accounts.google.com/"*) ;; *) echo "/challenge: $location"; fail=1;; esac
if [ $fail -ne 0 ]; then cat server.log; exit 1; fi
echo "Package smoke test passed ($version)."
