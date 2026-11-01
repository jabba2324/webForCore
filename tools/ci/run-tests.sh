#!/usr/bin/env bash
# Runs every test suite in Release after a build of WebFormsForCore.slnx (Release), as CI does.
# Usage: tools/ci/run-tests.sh [results-directory]
# Identity.Test needs SQL Server at 127.0.0.1,14333 (tools/lift/sqlserver.sh starts one locally; CI uses a service
# container on Linux and tools/ci/sqlserver-windows.ps1 on Windows). System.Drawing.Common.Tests runs Microsoft's Linux
# selection on both and fails only on failures that are not in tools/ci/known-failures/. A suite's failures do not stop the others; the exit code is non-zero if any suite failed.
set -uo pipefail
cd "$(dirname "$0")/../.."
OUT=$(realpath -m "${1:-TestResults}")
mkdir -p "$OUT"
export OPENSSL_ENABLE_SHA1_SIGNATURES=1
# .NET sizes its threads' stacks from this limit, and Web API's deep-nesting tests expect
# InsufficientExecutionStackException at 10,000 levels: the usual Linux default of 8 MB makes runs comparable.
ulimit -s 8192 2>/dev/null || echo "warning: could not set the stack limit to 8 MB"
echo "Stack limit: $(ulimit -s) KB"

SUITES=(
  Serialization.Tests System.Net.Http.Formatting.Test System.Web.Cors.Test System.Web.Http.Cors.Test
  System.Web.Razor.Test System.Web.Http.Test System.Web.Http.WebHost.Test System.Web.Http.Owin.Test
  Microsoft.Owin.Tests Microsoft.Owin.Host.SystemWeb.Tests Microsoft.Owin.Security.Tests System.Web.MonoTests
  System.Web.Mvc.Test Microsoft.Web.Mvc.Test System.Web.WebPages.Test System.Web.WebPages.Razor.Test
  System.Web.WebPages.Deployment.Test System.Web.Helpers.Test Microsoft.AspNet.SignalR.Tests Identity.Test
  WebApi.Gate.Tests Mvc.Gate.Tests IIS.Gate.Tests
)
DRAWING_FILTER="category!=failing&category!=nonlinuxtests&category!=nonnetcoreapptests&category!=OuterLoop&category!=IgnoreForCI&category!=nonunixtests"
# Windows (Git Bash, #46) runs the same selection: the GDI+ engine is webForCore's on both, not Windows' own.
KNOWN_FAILURES=tools/ci/known-failures/System.Drawing.Common.Tests.txt

failed=()
for s in "${SUITES[@]}"; do
  echo "::group::$s"
  if ! dotnet test "test/webForCore.$s/webForCore.$s.csproj" -c Release --no-build \
      --logger "trx;LogFileName=$s.trx" --results-directory "$OUT"; then
    failed+=("$s")
  fi
  echo "::endgroup::"
done

s=System.Drawing.Common.Tests
echo "::group::$s"
dotnet test "test/webForCore.$s/webForCore.$s.csproj" -c Release --no-build --filter "$DRAWING_FILTER" \
  --logger "trx;LogFileName=$s.trx" --results-directory "$OUT"
echo "::endgroup::"
python3 tools/ci/check_failures.py "$OUT/$s.trx" "$KNOWN_FAILURES" || failed+=("$s")

python3 tools/lift/trx_summary.py "$OUT"/*.trx > "$OUT/summary.md" || true
if [ ${#failed[@]} -gt 0 ]; then
  echo "Failed suites: ${failed[*]}"
  exit 1
fi
echo "All suites passed."
