#!/usr/bin/env bash
# Measures line and branch coverage of the production assemblies over every test suite (see docs/TESTING.md).
# Needs a Release build and dotnet-coverage (dotnet tool install -g dotnet-coverage). Usage: tools/ci/coverage.sh [out]
# Writes <out>/merged.cobertura.xml and <out>/coverage.md (per assembly, from tools/ci/coverage_summary.py).
set -uo pipefail
cd "$(dirname "$0")/../.."
OUT=$(realpath -m "${1:-TestResults/coverage}")
mkdir -p "$OUT"
export OPENSSL_ENABLE_SHA1_SIGNATURES=1
SUITES=(
  Serialization.Tests System.Net.Http.Formatting.Test System.Web.Cors.Test System.Web.Http.Cors.Test
  System.Web.Razor.Test System.Web.Http.Test System.Web.Http.WebHost.Test System.Web.Http.Owin.Test
  Microsoft.Owin.Tests Microsoft.Owin.Host.SystemWeb.Tests Microsoft.Owin.Security.Tests System.Web.MonoTests
  System.Web.Mvc.Test Microsoft.Web.Mvc.Test System.Web.WebPages.Test System.Web.WebPages.Razor.Test
  System.Web.WebPages.Deployment.Test System.Web.Helpers.Test Microsoft.AspNet.SignalR.Tests Identity.Test
  WebApi.Gate.Tests Mvc.Gate.Tests IIS.Gate.Tests System.Drawing.Common.Tests
)
DRAWING_FILTER="category!=failing&category!=nonlinuxtests&category!=nonnetcoreapptests&category!=OuterLoop&category!=IgnoreForCI&category!=nonunixtests"
for s in "${SUITES[@]}"; do
  filter=()
  [ "$s" = System.Drawing.Common.Tests ] && filter=(--filter "$DRAWING_FILTER")
  echo "== $s"
  dotnet-coverage collect "dotnet test test/webForCore.$s/webForCore.$s.csproj -c Release --no-build ${filter[*]:+--filter \"$DRAWING_FILTER\"}" \
    -f coverage -o "$OUT/$s.coverage" > "$OUT/$s.log" 2>&1
  grep -E '^(Passed|Failed)!' "$OUT/$s.log" | tail -1
done
dotnet-coverage merge "$OUT"/*.coverage -f cobertura -o "$OUT/merged.cobertura.xml"
python3 tools/ci/coverage_summary.py "$OUT/merged.cobertura.xml" > "$OUT/coverage.md"
cat "$OUT/coverage.md"
