#!/usr/bin/env bash
# Starts SQL Server 2022 (Developer edition) in a container for the tests that need a database (Identity.Test's
# Entity Framework tests): 127.0.0.1,14333, login sa, password below. Local test use only.
# Usage: tools/lift/sqlserver.sh [start|stop]
set -euo pipefail
NAME=webforcore-sqlserver
PASSWORD='webForCore-Test1!'
RUNTIME=$(command -v podman || command -v docker)
case "${1:-start}" in
  start)
    if ! "$RUNTIME" container exists "$NAME" 2>/dev/null && ! "$RUNTIME" inspect "$NAME" >/dev/null 2>&1; then
      "$RUNTIME" run -d --name "$NAME" -e ACCEPT_EULA=Y -e MSSQL_PID=Developer -e "MSSQL_SA_PASSWORD=$PASSWORD" \
        -p 127.0.0.1:14333:1433 mcr.microsoft.com/mssql/server:2022-latest >/dev/null
    else
      "$RUNTIME" start "$NAME" >/dev/null
    fi
    for i in $(seq 1 90); do
      log=$("$RUNTIME" logs "$NAME" 2>&1 || true)
      if [[ "$log" == *"SQL Server is now ready for client connections"* ]]; then echo "SQL Server ready on 127.0.0.1,14333"; exit 0; fi
      sleep 2
    done
    echo "SQL Server did not start"; exit 1 ;;
  stop) "$RUNTIME" stop "$NAME" >/dev/null ;;
esac
