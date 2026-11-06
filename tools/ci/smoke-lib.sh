# Sourced by the smoke test scripts: requests and waits that end (#215). Every request has a time limit, every wait a
# bound, and a timeout says what it was waiting for and prints the server's output, so a hang fails the step with the
# reason in its log instead of blocking it until the job is cancelled.

# http <curl arguments...>: curl -s with a time limit (60 s: the first request to a page compiles it). The body goes to
# stdout as curl writes it; a request that fails or times out says so on stderr and returns 0, so a check of its
# output fails with the request's own message beside it, rather than set -e ending the script without one.
http() {
  local rc=0
  curl -s --connect-timeout 10 --max-time "${SMOKE_REQUEST_TIMEOUT:-60}" "$@" || rc=$?
  # 23: the reader (grep -q) stopped reading once it had its match.
  if [ $rc -ne 0 ] && [ $rc -ne 23 ]; then
    if [ $rc -eq 28 ]; then echo "curl $*: no answer within ${SMOKE_REQUEST_TIMEOUT:-60} s" >&2
    else echo "curl $*: failed (curl exit code $rc)" >&2; fi
  fi
  return 0
}

# wait_ready <url> <pid> <log> [seconds]: waits until <url> answers with a success status (the host answers 503 until
# its worker serves), for at most [seconds] (120). Fails, printing <log>, if the server exits first or the time runs out.
wait_ready() {
  local url=$1 pid=$2 log=$3 limit=${4:-120} start=$SECONDS
  while [ $((SECONDS - start)) -lt "$limit" ]; do
    curl -sf --connect-timeout 5 --max-time 10 -o /dev/null "$url" && return 0
    if ! kill -0 "$pid" 2>/dev/null; then
      echo "The server (pid $pid) exited before $url answered. Its output ($log):"; cat "$log"; return 1
    fi
    sleep 1
  done
  echo "Timed out after $limit s waiting for $url to answer. The server's output ($log):"; cat "$log"; return 1
}

# stop_server <pid> <log>: stops the server and waits for it to exit, for at most 30 s after SIGTERM and 10 s after
# SIGKILL. Fails, printing <log>, if it is still running after the SIGTERM's 30 s.
stop_server() {
  local pid=$1 log=$2 i
  [ -n "$pid" ] || return 0
  kill "$pid" 2>/dev/null || true
  for i in $(seq 30); do
    if ! kill -0 "$pid" 2>/dev/null; then wait "$pid" 2>/dev/null || true; return 0; fi
    sleep 1
  done
  echo "The server (pid $pid) was still running 30 s after SIGTERM. Its output ($log):"; cat "$log"
  kill -9 "$pid" 2>/dev/null || true
  for i in $(seq 10); do kill -0 "$pid" 2>/dev/null || break; sleep 1; done
  return 1
}
