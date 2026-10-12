#!/usr/bin/env bash
# Exercises the gateway-readiness wait of docker/deploy.sh against a stub `curl`, `sleep` and compose,
# on a fake clock, so no daemon is touched and the test takes no real time. The gateway answers
# /gateway/ready 503 for its passive-health ReactivationPeriod (1 min) after the api restarts; the
# wait must (1) outlast that and return once the gateway turns 200, (2) return at once when it is
# already ready, (3) give up with a non-zero status, the 503 body and the gateway logs when it
# never recovers.
#
# Usage: test-deploy-gateway-wait.sh [repo-root]
set -euo pipefail

root="${1:-$(git rev-parse --show-toplevel)}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The block under test: from the budget assignment to the line that reports success.
sed -n '/^GATEWAY_READY_BUDGET_SECONDS=/,/^echo "\[deploy\] ok — gateway ready"$/p' "$root/docker/deploy.sh" > "$work/block.sh"
[ -s "$work/block.sh" ] || { echo "error: gateway wait block not found in docker/deploy.sh" >&2; exit 1; }

# The passive-health reactivation period the wait has to outlast, read from the gateway's own config.
reactivation="$(grep -m1 '"ReactivationPeriod"' "$root/backend/src/FinanceSentry.Gateway/appsettings.json" | grep -oE '[0-9]{2}:[0-9]{2}:[0-9]{2}')"
IFS=: read -r hh mm ss <<< "$reactivation"
reactivation_seconds=$((10#$hh * 3600 + 10#$mm * 60 + 10#$ss))

fail() { echo "FAIL: $1" >&2; exit 1; }

# run <seconds until the gateway turns ready; "never" = it does not>. Prints "<exit status> <fake seconds elapsed>".
run() {
  (
    SECONDS=0
    curl() { # fake clock: 503 until ready_at, 200 afterwards
      [[ $ready_at != never && $SECONDS -ge $ready_at ]] && return 0
      [[ " $* " == *" -sf "* ]] && return 22
      echo '{"status":"unavailable","clusters":[{"id":"api","available":0,"total":1}]}'
    }
    sleep() { SECONDS=$((SECONDS + $1)); }
    compose_stub() { echo "compose $*" >> "$work/compose.log"; }
    COMPOSE=(compose_stub)
    ready_at="$1"
    : > "$work/compose.log"
    set +e
    # The inner subshell's fake clock is invisible out here, so its exit trap reports it.
    ( set -e; trap 'echo "$SECONDS" > "$work/elapsed"' EXIT; source "$work/block.sh" ) > "$work/out" 2> "$work/err"
    echo "$? $(cat "$work/elapsed")"
  )
}

# 1. Ready only after the reactivation period has passed (plus a margin for the api's own startup).
read -r status elapsed <<< "$(run $((reactivation_seconds + 5)))"
[ "$status" = 0 ] || fail "gateway turning ready at $((reactivation_seconds + 5))s: wait gave up (status $status at ${elapsed}s)"
[ "$elapsed" -ge "$reactivation_seconds" ] || fail "returned at ${elapsed}s, before the gateway was ready"

# 2. Already ready: no waiting.
read -r status elapsed <<< "$(run 0)"
[ "$status" = 0 ] && [ "$elapsed" -eq 0 ] || fail "already-ready gateway: expected status 0 at 0s, got $status at ${elapsed}s"

# 3. Never ready: bounded, fails loudly, shows why.
read -r status elapsed <<< "$(run never)"
[ "$status" != 0 ] || fail "a gateway that never becomes ready must fail the wait"
[ "$elapsed" -gt "$reactivation_seconds" ] || fail "gave up after ${elapsed}s, inside the ${reactivation_seconds}s reactivation period"
[ "$elapsed" -lt 600 ] || fail "wait is not bounded (${elapsed}s)"
grep -q 'timed out' "$work/err" || fail "timeout error message missing"
grep -q '"available":0' "$work/err" || fail "the 503 readiness body must be printed on timeout"
grep -q 'logs --tail 60 gateway' "$work/compose.log" || fail "gateway logs must be printed on timeout"

echo "deploy.sh gateway readiness wait: ok (outlasts the ${reactivation_seconds}s reactivation period)"
