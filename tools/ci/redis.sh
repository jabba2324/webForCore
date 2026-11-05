#!/usr/bin/env bash
# Starts Redis in a container for the SignalR scale-out tests (IIS.Gate.Tests' ScaleoutTests): 127.0.0.1:16379, no
# password. Local test use only; CI uses a service container on Linux and tools/ci/redis-windows.ps1 on Windows.
# Usage: tools/ci/redis.sh [start|stop]
set -euo pipefail
NAME=webforcore-redis
RUNTIME=$(command -v podman || command -v docker)
case "${1:-start}" in
  start)
    if ! "$RUNTIME" container exists "$NAME" 2>/dev/null && ! "$RUNTIME" inspect "$NAME" >/dev/null 2>&1; then
      "$RUNTIME" run -d --name "$NAME" -p 127.0.0.1:16379:6379 docker.io/library/redis:7-alpine >/dev/null
    else
      "$RUNTIME" start "$NAME" >/dev/null
    fi
    for i in $(seq 1 30); do
      if [[ "$("$RUNTIME" exec "$NAME" redis-cli ping 2>/dev/null)" == "PONG" ]]; then echo "Redis ready on 127.0.0.1:16379"; exit 0; fi
      sleep 1
    done
    echo "Redis did not start"; exit 1 ;;
  stop) "$RUNTIME" stop "$NAME" >/dev/null ;;
esac
