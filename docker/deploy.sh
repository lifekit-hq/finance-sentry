#!/usr/bin/env bash
# Deploy finance-sentry on the VPS.
#
# - Decrypts docker/.env.sops → docker/.env (requires age key at SOPS_AGE_KEY_FILE
#   or ~/.config/sops/age/keys.txt).
# - Builds + starts the prod compose stack with linux/arm64 images.
# - Idempotent: safe to re-run on every push to main.
#
# Runs on a self-hosted GitHub Actions runner inside the repo working directory.
# Expects: docker, docker compose, sops, age installed on the host.

set -euo pipefail

cd "$(dirname "$0")/.."

KEYFILE="${SOPS_AGE_KEY_FILE:-$HOME/.config/sops/age/keys.txt}"
if [[ ! -f "$KEYFILE" ]]; then
  echo "error: no age key at $KEYFILE — cannot decrypt docker/.env.sops" >&2
  exit 1
fi

if ! command -v sops >/dev/null 2>&1; then
  echo "error: sops missing on PATH. Install from https://github.com/getsops/sops/releases" >&2
  exit 1
fi

echo "[deploy] decrypt docker/.env.sops"
SOPS_AGE_KEY_FILE="$KEYFILE" sops --decrypt --input-type dotenv --output-type dotenv docker/.env.sops > docker/.env
chmod 600 docker/.env

# --- Publish dashboards to the box's Grafana (lifekit-stack#133) ---------------
# The BOX's Grafana moved to lifekit-stack's compose project, but the dashboards
# stay this repo's: one home per dashboard, versioned next to the code they
# describe, and the same files the dev-compose Grafana provisions. lifekit-stack's
# Grafana reads them from a host directory, which this fills.
#
# Copy first, prune second, so a dashboard renamed or deleted here disappears
# there instead of double-listing forever — and so a failed copy never leaves
# the box with no dashboards at all.
#
# Best-effort on purpose: a dashboard that fails to copy must never fail a
# deploy of the application itself.
DASHBOARD_SRC="docker/observability/grafana/provisioning/dashboards"
DASHBOARD_DIR="${FINANCE_SENTRY_DASHBOARD_DIR:-/srv/finance-sentry/grafana-dashboards}"
echo "[deploy] publish Grafana dashboards -> $DASHBOARD_DIR"
if mkdir -p "$DASHBOARD_DIR" 2>/dev/null && cp "$DASHBOARD_SRC"/*.json "$DASHBOARD_DIR/"; then
  chmod 644 "$DASHBOARD_DIR"/*.json
  for published in "$DASHBOARD_DIR"/*.json; do
    [[ -e "$DASHBOARD_SRC/$(basename "$published")" ]] || rm -f "$published"
  done
else
  echo "[deploy] warn: cannot write $DASHBOARD_DIR — dashboards not refreshed" >&2
fi

# --- Platform contract (guardrail 1, spec 048) ---------------------------------
# The checker lives in lifekit-stack and is deployed to the box at this path; this
# product's deploy calls it on its own compose project (docs/platform-contract.md
# there). No existence check and no `|| true` on purpose: a missing checker fails
# the deploy rather than silently skipping the gate. No waivers.
CONTRACT=/srv/lifekit-stack/scripts/platform-contract.py
COMPOSE=(docker compose -f docker/docker-compose.prod.yml --env-file docker/.env)

# Static gate: every service's lifekit.contract.* declaration, before anything changes.
echo "[deploy] platform contract: declarations"
"${COMPOSE[@]}" config --format json | python3 "$CONTRACT" --static -

echo "[deploy] docker compose build + up"
"${COMPOSE[@]}" up -d --build --remove-orphans

echo "[deploy] prune dangling images (free disk on the VPS)"
docker image prune -f >/dev/null

echo "[deploy] wait for api health (via gateway — direct api port closed in 025 cutover)"
deadline=$((SECONDS + 120))
until curl -sf http://127.0.0.1:8080/api/v1/health >/dev/null 2>&1; do
  if [[ $SECONDS -gt $deadline ]]; then
    echo "error: api health check timed out after 120s" >&2
    docker compose -f docker/docker-compose.prod.yml logs --tail 60 api
    exit 1
  fi
  sleep 2
done

echo "[deploy] ok — api reachable via gateway on 127.0.0.1:8080"

# Runtime gate: probe the running containers (health, ready, metrics, scraped, logs).
# Bounded retry: the health wait above returns the moment the api answers, but the
# `scraped` item reads Prometheus' LAST scrape of each target, and a container
# recreated seconds ago is still `down` there until its next 15s scrape. Four
# attempts a scrape interval apart; the last failure exits 1 — the containers stay
# up (post-deploy assertion model), the job goes red.
CONTRACT_PROJECT="$("${COMPOSE[@]}" config --format json | python3 -c 'import json, sys; print(json.load(sys.stdin)["name"])')"
echo "[deploy] platform contract: running containers (project $CONTRACT_PROJECT)"
contract_attempts=4
for ((attempt = 1; attempt <= contract_attempts; attempt++)); do
  if python3 "$CONTRACT" --project "$CONTRACT_PROJECT"; then
    break
  fi
  if [[ $attempt -eq $contract_attempts ]]; then
    echo "error: platform contract failed after $contract_attempts attempts (table above)" >&2
    exit 1
  fi
  echo "[deploy] platform contract: attempt $attempt failed — retrying in 15s"
  sleep 15
done

# --- Uptime probe (issue #511) -------------------------------------------------
# Host-level cron so outage alerts don't depend on the stack being up. Telegram
# creds: prefer UPTIME_TELEGRAM_* from the decrypted docker/.env; fall back to the
# Ledger bot creds already provisioned in OpenClaw's env on this host.
echo "[deploy] install uptime probe (cron every 5 min)"
PROBE_DIR="$HOME/.fs-uptime"
mkdir -p "$PROBE_DIR"
cp docker/uptime-probe.sh "$PROBE_DIR/uptime-probe.sh"
chmod 700 "$PROBE_DIR/uptime-probe.sh"

if grep -qE '^UPTIME_TELEGRAM_BOT_TOKEN=' docker/.env 2>/dev/null; then
  grep -E '^UPTIME_TELEGRAM_(BOT_TOKEN|CHAT_ID)=' docker/.env > "$PROBE_DIR/probe.env"
elif sudo -n test -f /srv/openclaw/config/.env 2>/dev/null; then
  {
    printf 'UPTIME_TELEGRAM_BOT_TOKEN=%s\n' "$(sudo -n grep -E '^FINANCE_BOT_TOKEN=' /srv/openclaw/config/.env | cut -d= -f2-)"
    printf 'UPTIME_TELEGRAM_CHAT_ID=%s\n' "$(sudo -n grep -E '^TELEGRAM_OWNER_USER_ID=' /srv/openclaw/config/.env | cut -d= -f2-)"
  } > "$PROBE_DIR/probe.env"
else
  echo "[deploy] warn: no Telegram creds for uptime probe — probe will no-op" >&2
fi
if [[ -f "$PROBE_DIR/probe.env" ]]; then
  chmod 600 "$PROBE_DIR/probe.env"
fi

# grep exits 1 on an empty/absent crontab — the `|| true` keeps errexit+pipefail from
# killing the list mid-pipe and clobbering the crontab with empty input (broke deploy once).
{
  crontab -l 2>/dev/null | grep -v 'fs-uptime' || true
  echo "*/5 * * * * $PROBE_DIR/uptime-probe.sh >> $PROBE_DIR/probe.log 2>&1"
} | crontab -
echo "[deploy] uptime probe installed"
