#!/usr/bin/env bash
# Builds and runs tools/ci/package-smoke, an application that uses webForCore through its packages only, against the
# packages in <package folder> (drawingForCore's feed for System.Drawing, nuget.org for everything else), then requests
# its pages and checks where the W3C log went.
# Usage: tools/ci/package-smoke.sh <package folder> <version>
set -euo pipefail
# pwd -W: the Windows form of the path under Git Bash (#46), since it is written into nuget.config.
packages=$(cd "$1" && { pwd -W 2>/dev/null || pwd; }); version=$2
here=$(cd "$(dirname "$0")" && pwd)
base=$(mktemp -d); work=$base/site; mkdir "$work"
# The host's worker stops when the host does, a moment later: on Windows its folder can't be removed until it has
# (rm: Device or resource busy), so the removal is retried for up to 30 s.
cleanup() {
  kill $server 2>/dev/null || true
  for i in $(seq 30); do rm -rf "$base" 2>/dev/null && return 0; sleep 1; done
  rm -rf "$base"
}
trap cleanup EXIT
cp -r "$here/package-smoke/." "$work"
cd "$work"
cat > nuget.config <<CONFIG
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="webForCore" value="$packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="drawingForCore" value="https://xforcore.github.io/drawingForCore/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="webForCore"><package pattern="webForCore" /><package pattern="webForCore.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
    <packageSource key="drawingForCore"><package pattern="drawingForCore" /><package pattern="drawingForCore.*" /></packageSource>
  </packageSourceMapping>
</configuration>
CONFIG
# The SDK of the target framework (the run is in a temporary folder, outside this repository's global.json).
echo '{"sdk":{"version":"10.0.100","rollForward":"latestFeature"}}' > global.json
dotnet build Smoke.csproj -c Release -p:WebForCoreVersion="$version" -nodeReuse:false
# Under webforcore-host, which the webForCore package puts in the output (#56), as an application runs.
dotnet bin_dotnet/webforcore-host/webforcore-host.dll --urls http://127.0.0.1:5199 > server.log 2>&1 &
server=$!
# -f: the host answers 503 until the worker serves.
for i in $(seq 120); do curl -sf -o /dev/null http://127.0.0.1:5199/Default.aspx && break; sleep 1; done
fail=0
curl -s http://127.0.0.1:5199/Default.aspx | grep -q "webForCore package smoke test: 10/4.8" || { echo "Default.aspx: unexpected response"; fail=1; }
grep -q "(worker 1 of webforcore-host)" server.log || { echo "server.log: the application didn't run as webforcore-host's worker"; fail=1; }
curl -s http://127.0.0.1:5199/Greet.aspx | grep -q "hello from App_Code" || { echo "Greet.aspx: unexpected response"; fail=1; }
# Markup.cs is compiled into the app against the packages' System.Web (issue #7: HttpUtility and IHtmlString, CS0433).
curl -s http://127.0.0.1:5199/Encode.aspx | grep -q "<b>&lt;compiled&gt;</b>" || { echo "Encode.aspx: unexpected response"; fail=1; }
# Service.cs, compiled into the app, names System.Web.Services.Description types, which the System.Web.Services.Description
# package that CoreWCF brings defines too (issue #34: CS0433, so the build itself failed); Ping.ashx serves it.
curl -s http://127.0.0.1:5199/Ping.ashx | grep -q "ping Literal" || { echo "Ping.ashx: unexpected response"; fail=1; }
# A Repeater with ItemType needs System.Web.DynamicData, which the webForCore package brings (issue #8).
curl -s http://127.0.0.1:5199/Typed.aspx | grep -q "\[typed\]\[repeater\]" || { echo "Typed.aspx: unexpected response"; fail=1; }
# Web.config's <compilation><assemblies> lists System.Data.Linq 3.5.0.0, which the webForCore package brings, and
# Linq.aspx compiles against it (issue #142: every page was a Configuration Error, "Could not load file or assembly").
curl -s http://127.0.0.1:5199/Linq.aspx | grep -q "binary 3 bytes, System.Data.Linq 4.0.0.0" || { echo "Linq.aspx: unexpected response"; curl -s http://127.0.0.1:5199/Linq.aspx | head -5; fail=1; }
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

# The same application with its targeting pack taken from the NuGet cache (#55), as when an SDK newer than the target
# framework builds it: the packages must drop the framework's System.Web.HttpUtility wherever the pack is (CS0433 for
# HttpUtility and IHtmlString in Markup.cs otherwise). An empty NetCoreTargetingPackRoot makes the SDK download the pack
# (its package pruning data is in the packs folder too: AllowMissingPrunePackageData).
nugetpack=$base/nuget-pack; mkdir "$nugetpack" "$base/no-packs"
cp -r "$here/package-smoke/." "$nugetpack"; cp nuget.config global.json "$nugetpack"
(cd "$nugetpack" && dotnet build Smoke.csproj -c Release -p:WebForCoreVersion="$version" -p:NetCoreTargetingPackRoot="$base/no-packs" -p:AllowMissingPrunePackageData=true -nodeReuse:false)

# tools/ci/package-smoke-mvc: MVC 5 and OWIN with the third-party packages applications have, as they have them.
mvc=$base/mvc; mkdir "$mvc"
cp -r "$here/package-smoke-mvc/." "$mvc"
cp nuget.config "$mvc"
cd "$mvc"
echo '{"sdk":{"version":"10.0.100","rollForward":"latestFeature"}}' > global.json
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
# Nothing of Microsoft's System.Data.SqlClient package, which Entity Framework brings, is in the output or deps.json, its
# runtimes/unix and runtimes/win builds included (issue #41: they are at other paths than webForCore's, so the SDK's
# conflict resolution doesn't remove them).
if grep -q -E '"System\.Data\.SqlClient/|netcoreapp2\.1/System\.Data\.SqlClient\.dll' bin_dotnet/SmokeMvc.deps.json \
    || [ -n "$(find bin_dotnet -path '*netcoreapp2.1*' -name System.Data.SqlClient.dll)" ]; then
  echo "Microsoft's System.Data.SqlClient package is in the output:"
  grep -n 'System.Data.SqlClient' bin_dotnet/SmokeMvc.deps.json | head -20
  fail=1
fi
# One version of each IdentityModel package, the one CoreWCF brings, although IdentityServer3.Contrib.AccessTokenValidation
# asks for 5.6 (a mix of IdentityModel 5 and 8 is what an application that kept Microsoft's Microsoft.Owin.Security.OpenIdConnect
# got; with the packages.config list of IdentityModel 5 references, restore failed with NU1605), and webForCore's
# Microsoft.Owin.Security.Jwt, not the 4.0.1 the third-party package depends on.
identitymodel=$(grep -o -E '"(Microsoft|System)\.IdentityModel[A-Za-z.]*/[0-9.]+"' bin_dotnet/SmokeMvc.deps.json | sed 's|.*/||; s|"||' | sort -u)
[ "$(echo "$identitymodel" | wc -l)" = 1 ] || { echo "deps.json: more than one IdentityModel version: $identitymodel"; fail=1; }
if grep -q -E '"lib/net4[0-9]*/Microsoft\.Owin\.Security\.(Jwt|OpenIdConnect)\.dll"' bin_dotnet/SmokeMvc.deps.json; then
  echo "deps.json: Microsoft's Katana assemblies replaced webForCore's:"
  grep -o -E '"lib/net4[0-9]*/Microsoft\.Owin\.Security\.[A-Za-z]*\.dll"' bin_dotnet/SmokeMvc.deps.json | sort -u
  fail=1
fi
dotnet bin_dotnet/webforcore-host/webforcore-host.dll --urls http://127.0.0.1:5198 > server.log 2>&1 &
server=$!
for i in $(seq 120); do curl -sf -o /dev/null http://127.0.0.1:5198/ && break; sleep 1; done
# A Razor view renders although Unity.Mvc brings Microsoft.AspNet.Razor 3.2.7 (issue #23: it replaced webForCore's in
# the output, and every view failed with a MissingMethodException).
curl -s http://127.0.0.1:5198/Plain | grep -q "plain view, rendered by Razor" || { echo "/Plain: unexpected response"; curl -s http://127.0.0.1:5198/Plain | grep -i -m2 '<title>'; fail=1; }
# System.Web.Helpers, listed in Web.config and used by the view, comes with webForCore.Web.Mvc, as with
# Microsoft.AspNet.Mvc (issue #28: Configuration Error, "Could not load file or assembly 'System.Web.Helpers'").
curl -s http://127.0.0.1:5198/Plain | grep -q '{"helpers":true}' || { echo "/Plain: System.Web.Helpers missing"; fail=1; }
# webForCore's SignalR and DataVisualization have Microsoft's assembly versions, so Microsoft's SignalR SQL Server
# scale-out (built against SignalR 2.4.3) compiles and binds (issue #40: CS1705 against SignalR.Core 0.0.0.0, JabbR).
curl -s http://127.0.0.1:5198/Versions | grep -q "SignalR 2.4.3.0, scale-out 2.4.3.0, DataVisualization 4.0.0.0" || { echo "/Versions: unexpected response"; curl -s http://127.0.0.1:5198/Versions | head -c 300; echo; fail=1; }
# webForCore's System.Data.SqlClient runs, Entity Framework binds to it, and Assembly.GetTypes() works on it and on every
# other loaded assembly (issue #41: on Microsoft's it threw, so a scan of every loaded assembly failed, as Opserver's did).
scan=$(curl -s http://127.0.0.1:5198/Scan)
case "$scan" in "SqlClient 4.901."*", GetTypes ok, Entity Framework binds: True, scan failures: 0 ") ;; *) echo "/Scan: $scan"; fail=1;; esac
# Unity, started from Application_Start (WebActivatorEx's start methods don't run on webForCore, issue #21; the original
# package is in the build all the same and must not break it), constructs the controller, and its Razor view renders
# although Unity.Mvc brings Microsoft.AspNet.Razor 3.2.7 (issue #23: it replaced webForCore's, MissingMethodException).
home=$(curl -s http://127.0.0.1:5198/)
echo "$home" | grep -q "hello from Unity, rendered by Razor" || { echo "/: unexpected response"; echo "$home" | head -20; fail=1; }
# Katana 3.0.1's Google provider redirects a challenge to Google (issue #18: it needs System.Net.Http.WebRequest, and
# every request failed).
location=$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' http://127.0.0.1:5198/challenge)
case "$location" in "302 https://accounts.google.com/"*) ;; *) echo "/challenge: $location"; fail=1;; esac
# webForCore's OpenID Connect middleware (Entra ID's settings: code id_token, form_post) redirects a challenge to the
# authorize endpoint, built against IdentityModel 8 (its telemetry parameter says which platform).
oidc=$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' http://127.0.0.1:5198/oidc/challenge)
case "$oidc" in "302 https://login.microsoftonline.com/smoke-tenant/oauth2/v2.0/authorize?client_id=smoke-client-id&"*"response_mode=form_post"*"x-client-SKU=ID_NET10_0"*) ;; *) echo "/oidc/challenge: $oidc"; fail=1;; esac
# Web API OData's model builder, built for .NET Framework, maps LINQ to SQL's Binary, from the System.Data.Linq the
# webForCore package brings (issue #142: without it, GetEdmModel() threw and OData applications failed at start, F68).
odata=$(curl -s http://127.0.0.1:5198/ODataModel)
case "$odata" in "Blob: Edm.Binary, Bytes: Edm.Binary, Id: Edm.Int32; System.Data.Linq, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089") ;; *) echo "/ODataModel: $odata" | head -c 400; echo; fail=1;; esac
if [ $fail -ne 0 ]; then cat server.log; exit 1; fi
echo "Package smoke test passed ($version)."
