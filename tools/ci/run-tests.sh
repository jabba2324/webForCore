#!/usr/bin/env bash
# Runs every test suite in Release after a build of WebFormsForCore.slnx (Release), as CI does.
# Usage: tools/ci/run-tests.sh [results-directory]
# Identity.Test and System.Data.Linq.Tests need SQL Server at 127.0.0.1,14333 (tools/ci/sqlserver.sh starts one locally; CI uses a service
# container on Linux and tools/ci/sqlserver-windows.ps1 on Windows). IIS.Gate.Tests' SignalR scale-out tests also need Redis at
# 127.0.0.1:16379 (tools/ci/redis.sh; CI: a service container on Linux, tools/ci/redis-windows.ps1 on Windows). System.Drawing's own tests are drawingForCore's
# (https://github.com/xforcore/drawingForCore). A suite's failures do not stop the others; the exit code is non-zero if
# any suite failed.
set -uo pipefail
cd "$(dirname "$0")/../.."
OUT=$(realpath -m "${1:-TestResults}")
mkdir -p "$OUT"
export OPENSSL_ENABLE_SHA1_SIGNATURES=1
# Test names have non-ASCII characters (∞), and Windows' Python writes and reads its code page otherwise (#46).
export PYTHONUTF8=1
# .NET sizes its threads' stacks from this limit, and Web API's deep-nesting tests expect
# InsufficientExecutionStackException at 10,000 levels: the usual Linux default of 8 MB makes runs comparable.
ulimit -s 8192 2>/dev/null || echo "warning: could not set the stack limit to 8 MB"
echo "Stack limit: $(ulimit -s) KB"

SUITES=(
  Serialization.Tests System.Net.Http.Formatting.Test System.Web.Cors.Test System.Web.Http.Cors.Test
  System.Web.Razor.Test System.Web.Http.Test System.Web.Http.WebHost.Test System.Web.Http.Owin.Test
  Microsoft.Owin.Tests Microsoft.Owin.Host.SystemWeb.Tests Microsoft.Owin.Security.Tests System.Web.MonoTests
  System.Web.Mvc.Test Microsoft.Web.Mvc.Test System.Web.WebPages.Test System.Web.WebPages.Razor.Test
  System.Web.WebPages.Deployment.Test System.Web.Helpers.Test Microsoft.AspNet.SignalR.Tests
  Microsoft.AspNet.SignalR.StackExchangeRedis.Tests Microsoft.AspNet.SignalR.Redis.Tests Microsoft.AspNet.SignalR.SqlServer.Tests Identity.Test
  System.Data.SqlClient.Tests System.Data.Linq.Tests System.Data.Linq.MonoTests
  WebApi.Gate.Tests Mvc.Gate.Tests Oidc.Gate.Tests IIS.Gate.Tests Host.Tests
)

failed=()
for s in "${SUITES[@]}"; do
  echo "::group::$s"
  if ! dotnet test "test/webForCore.$s/webForCore.$s.csproj" -c Release --no-build \
      --logger "trx;LogFileName=$s.trx" --results-directory "$OUT"; then
    failed+=("$s")
  fi
  echo "::endgroup::"
done

dotnet run tools/ci/trx_summary.cs -- "$OUT"/*.trx > "$OUT/summary.md" || true
if [ ${#failed[@]} -gt 0 ]; then
  echo "Failed suites: ${failed[*]}"
  exit 1
fi
echo "All suites passed."
