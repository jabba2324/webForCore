#!/usr/bin/env bash
# #195: builds tools/ci/package-smoke-sql twice against the packages in <package folder>, as package-smoke.sh builds its
# applications: with webForCore's System.Data.SqlClient (webForCore.Data.SqlClient, the default), and with Microsoft's
# System.Data.SqlClient 4.9.1 package, which a library the application references depends on. Each build must have
# exactly that one in its output and deps.json, the same deps.json when built again from scratch, and serve Sql.aspx and
# Session.aspx: webForCore's SQL Server features, and Microsoft.Data.SqlClient 5 beside them, against SQL Server
# (127.0.0.1,14333, tools/ci/sqlserver.sh; the CI jobs' server).
# Usage: tools/ci/package-smoke-sql.sh <package folder> <version>
set -euo pipefail
packages=$(cd "$1" && { pwd -W 2>/dev/null || pwd; }); version=$2
here=$(cd "$(dirname "$0")" && pwd)
. "$here/smoke-lib.sh"
base=$(mktemp -d)
server=
cleanup() {
  [ -z "$server" ] || kill $server 2>/dev/null || true
  for i in $(seq 30); do rm -rf "$base" 2>/dev/null && return 0; sleep 1; done
  rm -rf "$base"
}
trap cleanup EXIT
fail=0
for variant in webForCore Microsoft; do
  work=$base/$variant; mkdir "$work"
  cp -r "$here/package-smoke-sql/." "$work"
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
  echo '{"sdk":{"version":"10.0.100","rollForward":"latestFeature"}}' > global.json
  dotnet build SmokeSql.csproj -c Release -p:WebForCoreVersion="$version" -p:SmokeSqlClient=$variant -nodeReuse:false | tee build.log
  deps=bin_dotnet/SmokeSql.deps.json
  # The one System.Data.SqlClient: its package's runtime assets in deps.json, none of the other's.
  if [ $variant = Microsoft ]; then keep='System.Data.SqlClient/4.9.1'; drop='webForCore.Data.SqlClient/'; else keep='webForCore.Data.SqlClient/'; drop='System.Data.SqlClient/'; fi
  # deps.json's "targets" section: each library and its runtime/runtimeTargets assets (a library without assets is listed
  # with none, and doesn't count).
  assets=$(dotnet run --file "$here/deps_assets.cs" -- "$deps" System.Data.SqlClient.dll)
  echo "$variant: System.Data.SqlClient.dll in deps.json from: $assets"
  echo "$assets" | grep -q "^$keep" || { echo "$variant: deps.json has no System.Data.SqlClient from $keep"; fail=1; }
  if echo "$assets" | grep -q "^$drop"; then echo "$variant: deps.json has System.Data.SqlClient from $drop"; fail=1; fi
  grep -q "webForCore: System.Data.SqlClient is" build.log || [ $variant = webForCore ] || { echo "$variant: the build didn't say which System.Data.SqlClient it kept"; fail=1; }
  # Deterministic: the same deps.json when built again from scratch.
  cp "$deps" deps.first.json
  rm -rf bin_dotnet obj SharedData/bin SharedData/obj
  dotnet build SmokeSql.csproj -c Release -p:WebForCoreVersion="$version" -p:SmokeSqlClient=$variant -nodeReuse:false > build2.log || { cat build2.log; exit 1; }
  cmp -s deps.first.json "$deps" || { echo "$variant: deps.json differs between two builds"; diff deps.first.json "$deps" | head -20; fail=1; }

  dotnet bin_dotnet/webforcore-host/webforcore-host.dll --urls http://127.0.0.1:5197 > server.log 2>&1 &
  server=$!
  wait_ready http://127.0.0.1:5197/Session.aspx $server server.log
  sql=$(http http://127.0.0.1:5197/Sql.aspx)
  echo "$variant: Sql.aspx:"; echo "$sql"
  if [ $variant = Microsoft ]; then
    expected_client="System.Data.SqlClient: 4.6.1.6, file 4.900."; getTypes=ReflectionTypeLoadException; shared=2
  else
    expected_client="System.Data.SqlClient: 4.6.1.6, file 4.901."; getTypes=ok; shared="(none)"
  fi
  for line in "$expected_client" "GetTypes: $getTypes" "System.Web binds: True" "DbProviderFactories: True" "SqlDataSource: one,two" \
      "membership: Success, True" "roles: True" "profile: green" "LINQ to SQL: 2" "SqlCacheDependency: removed" \
      "Microsoft.Data.SqlClient 5: 2" "SqlDataSource on Microsoft.Data.SqlClient: 2" "shared library: $shared"; do
    echo "$sql" | grep -qF -- "$line" || { echo "$variant: Sql.aspx has no \"$line\""; fail=1; }
  done
  jar=$work/cookies
  http -c "$jar" -b "$jar" http://127.0.0.1:5197/Session.aspx > /dev/null
  session=$(http -c "$jar" -b "$jar" http://127.0.0.1:5197/Session.aspx)
  [ "$session" = "session: 2" ] || { echo "$variant: Session.aspx: $session"; fail=1; }
  stop_server $server server.log; server=
  if [ $fail -ne 0 ]; then cat server.log; exit 1; fi
  cd "$base"
done
echo "SQL package smoke test passed ($version)."
