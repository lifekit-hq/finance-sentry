#!/usr/bin/env bash
# Deploy finance-sentry on the VPS.
#
# - Decrypts docker/.env.sops → docker/.env (requires age key at SOPS_AGE_KEY_FILE
#   or ~/.config/sops/age/keys.txt).
# - Pulls the images Docker Build published for one commit (IMAGE_TAG = its full SHA) and
#   starts the prod compose stack on them without building.
# - Idempotent: safe to re-run; re-running with an older SHA is a rollback.
#
# Runs on a self-hosted GitHub Actions runner inside the repo working directory, after the
# workflow has logged in to ghcr. Expects: docker, docker compose, sops, age installed on the host.
#
# Break-glass (ghcr or CI unavailable): the compose file keeps a `build:` block for every
# published service, so the stack can be built from the checked-out source on the host instead:
#
#   export NODE_AUTH_TOKEN=$(gh auth token)   # read:packages — the frontend build's npm registry
#   IMAGE_TAG=local docker compose -f docker/docker-compose.prod.yml --env-file docker/.env \
#     up -d --build --remove-orphans
#
# That skips this script's gates (platform contract, health wait), so run them by hand or re-run
# a normal deploy once the images publish again.

set -euo pipefail

cd "$(dirname "$0")/.."

# Services Docker Build publishes to ghcr by commit SHA.
PUBLISHED=(api mcp gateway frontend)

IMAGE_TAG="${IMAGE_TAG:-}"
if ! [[ $IMAGE_TAG =~ ^[0-9a-f]{40}$ ]]; then
  echo "error: IMAGE_TAG must be the full commit SHA whose published images to deploy (got '$IMAGE_TAG')" >&2
  exit 1
fi
export IMAGE_TAG

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

COMPOSE=(docker compose -f docker/docker-compose.prod.yml --env-file docker/.env)

# Fail before anything changes, naming every missing image, when the commit has no published
# images (Docker Build failed or never ran for it, or the versions were pruned).
echo "[deploy] check published images for $IMAGE_TAG"
images="$("${COMPOSE[@]}" config --format json | python3 -c \
  'import json, sys; s = json.load(sys.stdin)["services"]; print("\n".join(s[n]["image"] for n in sys.argv[1:]))' \
  "${PUBLISHED[@]}")"
missing=0
for image in $images; do
  if ! docker manifest inspect "$image" >/dev/null 2>&1; then
    echo "error: image $image not found — was it published for this commit?" >&2
    missing=1
  fi
done
if [[ $missing -ne 0 ]]; then
  exit 1
fi

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

# Static gate: every service's lifekit.contract.* declaration, before anything changes.
echo "[deploy] platform contract: declarations"
"${COMPOSE[@]}" config --format json | python3 "$CONTRACT" --static -

# The API reaches the org identity provider (Logto) by name over this external network. lifekit-stack's
# deploy creates it too; creating it idempotently here keeps the deploy order irrelevant.
docker network inspect identity-oidc >/dev/null 2>&1 || docker network create identity-oidc >/dev/null

echo "[deploy] pull published images @ $IMAGE_TAG"
"${COMPOSE[@]}" pull "${PUBLISHED[@]}"

echo "[deploy] docker compose up (no build)"
"${COMPOSE[@]}" up -d --no-build --remove-orphans

echo "[deploy] prune dangling images (free disk on the VPS)"
docker image prune -f >/dev/null

echo "[deploy] wait for api health (via gateway — direct api port closed in 025 cutover)"
deadline=$((SECONDS + 120))
until curl -sf http://127.0.0.1:8080/api/v1/health >/dev/null 2>&1; do
  if [[ $SECONDS -gt $deadline ]]; then
    api_logs="$(docker compose -f docker/docker-compose.prod.yml logs api 2>&1 || true)"
    if grep -q StartupMigrationException <<<"$api_logs"; then
      echo "error: STARTUP MIGRATION FAILURE — the api refused to start rather than serve a half-migrated schema" >&2
      echo "error: see docs/OPERATIONS_RUNBOOK.md §8 (Startup Migration Failure)" >&2
      grep "STARTUP MIGRATION FAILURE" <<<"$api_logs" | tail -n 1 >&2 || true
      exit 1
    fi
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
# attempts a scrape interval apart; the last failure exits 1 at the end of the
# script — the containers stay up (post-deploy
# assertion model), the job goes red.
CONTRACT_PROJECT="$("${COMPOSE[@]}" config --format json | python3 -c 'import json, sys; print(json.load(sys.stdin)["name"])')"
echo "[deploy] platform contract: running containers (project $CONTRACT_PROJECT)"
contract_attempts=4
contract_ok=false
for ((attempt = 1; attempt <= contract_attempts; attempt++)); do
  if python3 "$CONTRACT" --project "$CONTRACT_PROJECT"; then
    contract_ok=true
    break
  fi
  if [[ $attempt -eq $contract_attempts ]]; then
    break
  fi
  echo "[deploy] platform contract: attempt $attempt failed — retrying in 15s"
  sleep 15
done

if [[ $contract_ok != true ]]; then
  echo "error: platform contract failed after $contract_attempts attempts (table above)" >&2
  exit 1
fi
