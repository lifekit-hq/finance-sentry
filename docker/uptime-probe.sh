#!/usr/bin/env bash
# Uptime probe (issue #511) — synthetic monitoring for the deployed stack.
#
# Runs as the `uptime-probe` compose service: its own minimal container, separate from the
# api/gateway, so it still fires when they are down (the in-app 023 alerting path can't report
# its own outage). See docker/Dockerfile.uptime-probe.
#
# Behavior:
# - Every UPTIME_INTERVAL_SECONDS (default 300) probes the gateway health endpoint over the
#   compose network (default http://gateway:8080/api/v1/health).
# - After UPTIME_FAIL_THRESHOLD consecutive failures, sends ONE Telegram alert (Ledger bot,
#   direct Bot API — no OpenClaw cognition involved) and stays quiet until recovery,
#   which sends a single ✅ note and re-arms.
# - Credentials come from the container environment (UPTIME_TELEGRAM_* in docker/.env);
#   without them the probe idles and logs why rather than half-working.
# - State (consecutive failures, alerted flag) lives in UPTIME_STATE_DIR, a named volume, so a
#   container restart mid-outage neither re-alerts nor forgets the failure count.
# - UPTIME_ONCE=1 runs a single probe and exits (used for ad-hoc runs and tests).
set -uo pipefail

STATE_DIR="${UPTIME_STATE_DIR:-/var/lib/uptime-probe}"
STATE_FILE="$STATE_DIR/state"
HEARTBEAT_FILE="$STATE_DIR/heartbeat"
TARGET="${UPTIME_TARGET:-http://gateway:8080/api/v1/health}"
FAIL_THRESHOLD="${UPTIME_FAIL_THRESHOLD:-2}"
INTERVAL="${UPTIME_INTERVAL_SECONDS:-300}"

send_telegram() {
  curl -sS --max-time 10 \
    "https://api.telegram.org/bot${UPTIME_TELEGRAM_BOT_TOKEN}/sendMessage" \
    -d "chat_id=${UPTIME_TELEGRAM_CHAT_ID}" \
    --data-urlencode "text=$1" >/dev/null
}

probe_once() {
  local fails=0 alerted=0
  if [[ -f "$STATE_FILE" ]]; then read -r fails alerted < "$STATE_FILE" || true; fi

  if curl -sf --max-time 10 "$TARGET" >/dev/null 2>&1; then
    if [[ "$alerted" == 1 ]]; then
      send_telegram "✅ finance-sentry is back: health probe green again ($TARGET)."
      echo "$(date -u +%FT%TZ) recovered after $fails failures"
    fi
    printf '0 0\n' > "$STATE_FILE"
  else
    fails=$((fails + 1))
    echo "$(date -u +%FT%TZ) probe FAILED ($fails consecutive, target $TARGET)" >&2
    if [[ "$fails" -ge "$FAIL_THRESHOLD" && "$alerted" == 0 ]]; then
      if send_telegram "🔴 finance-sentry DOWN: health probe failed $fails times in a row ($TARGET). Check the VPS."; then
        alerted=1
      fi
    fi
    printf '%s %s\n' "$fails" "$alerted" > "$STATE_FILE"
  fi
}

mkdir -p "$STATE_DIR"

if [[ -z "${UPTIME_TELEGRAM_BOT_TOKEN:-}" || -z "${UPTIME_TELEGRAM_CHAT_ID:-}" ]]; then
  echo "$(date -u +%FT%TZ) UPTIME_TELEGRAM_* not set — probe idle, no alerts will be sent" >&2
  [[ "${UPTIME_ONCE:-0}" == 1 ]] && exit 0
  # Stay up (and healthy) instead of exiting, so restart: unless-stopped doesn't crash-loop.
  while true; do touch "$HEARTBEAT_FILE"; sleep "$INTERVAL"; done
fi

if [[ "${UPTIME_ONCE:-0}" == 1 ]]; then
  probe_once
  exit 0
fi

while true; do
  probe_once
  touch "$HEARTBEAT_FILE"
  sleep "$INTERVAL"
done
