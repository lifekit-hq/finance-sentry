#!/usr/bin/env bash
# Deploy finance-sentry on the VPS.
#
# - Decrypts docker/.env.sops → docker/.env with the age key at SOPS_AGE_KEY_FILE, by default
#   firstmate's ~/.config/sops/age/firstmate.agekey (a recipient beside the captain in .sops.yaml),
#   so the deploy never uses the captain's own key.
# - Persists the non-secret hostname/edge values (JWT_ISSUER, FRONTEND_BASE_URL,
#   TRUELAYER_FRONTEND_REDIRECT_BASE, EDGE_SUBNET, EDGE_IP_RANGE, EDGE_BRIDGE_IP, EDGE_GATEWAY_IP)
#   to the gitignored docker/.env.deploy. The deploy workflow supplies them from repository
#   variables; a run without them (manual re-run, break-glass) reuses the last-deployed file.
#   The compose file requires every one, so a missing value fails loudly at interpolation.
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
#   IMAGE_TAG=local docker compose -f docker/docker-compose.prod.yml \
#     --env-file docker/.env --env-file docker/.env.deploy up -d --build --remove-orphans
#
# Any other host-side compose command (restart, logs, ps) takes the same two --env-file flags.
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

KEYFILE="${SOPS_AGE_KEY_FILE:-$HOME/.config/sops/age/firstmate.agekey}"
if [[ ! -f "$KEYFILE" ]]; then
  echo "error: no age key at $KEYFILE — cannot decrypt docker/.env.sops (set SOPS_AGE_KEY_FILE to another recipient's key)" >&2
  exit 1
fi

if ! command -v sops >/dev/null 2>&1; then
  echo "error: sops missing on PATH. Install from https://github.com/getsops/sops/releases" >&2
  exit 1
fi

echo "[deploy] decrypt docker/.env.sops"
SOPS_AGE_KEY_FILE="$KEYFILE" sops --decrypt --input-type dotenv --output-type dotenv docker/.env.sops > docker/.env
chmod 600 docker/.env

DEPLOY_ENV_FILE=docker/.env.deploy
DEPLOY_ENV_VARS=(JWT_ISSUER FRONTEND_BASE_URL TRUELAYER_FRONTEND_REDIRECT_BASE EDGE_SUBNET EDGE_IP_RANGE EDGE_BRIDGE_IP EDGE_GATEWAY_IP)
supplied=()
unsupplied=()
for name in "${DEPLOY_ENV_VARS[@]}"; do
  if [[ -n "${!name:-}" ]]; then supplied+=("$name"); else unsupplied+=("$name"); fi
done
if [[ ${#supplied[@]} -eq ${#DEPLOY_ENV_VARS[@]} ]]; then
  echo "[deploy] persist hostname/edge values -> $DEPLOY_ENV_FILE"
  ( umask 077; for name in "${DEPLOY_ENV_VARS[@]}"; do printf '%s=%s\n' "$name" "${!name}"; done > "$DEPLOY_ENV_FILE" )
elif [[ ${#supplied[@]} -gt 0 ]]; then
  echo "error: hostname/edge values only partly supplied; missing: ${unsupplied[*]}" >&2
  exit 1
elif [[ -f "$DEPLOY_ENV_FILE" ]]; then
  echo "[deploy] hostname/edge values not supplied — reusing last-deployed $DEPLOY_ENV_FILE"
else
  echo "error: hostname/edge values not supplied and no $DEPLOY_ENV_FILE from a previous deploy: ${DEPLOY_ENV_VARS[*]}" >&2
  exit 1
fi

COMPOSE=(docker compose -f docker/docker-compose.prod.yml --env-file docker/.env --env-file "$DEPLOY_ENV_FILE")

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

# --- One-shot project rename: "docker" -> "finance-sentry" ---------------------
# The stack used to run as compose project "docker" (the directory name) and is now pinned to
# `name: finance-sentry`. container_name is fixed, so the old project's containers must be gone
# before the new project's can start. Its named volumes are pinned in the compose file to their
# docker_* names, so the new project mounts the same data. `down` without -v removes only
# containers and the project's networks; it never touches a volume. Once the old project is gone
# this finds nothing and does nothing. Only a project whose containers came from this compose
# file counts: another stack that happens to live in a directory named "docker" is left alone.
legacy_files="$(docker ps -a --filter label=com.docker.compose.project=docker \
  --format '{{.Label "com.docker.compose.project.config_files"}}')"
if grep -q 'docker-compose\.prod\.yml' <<<"$legacy_files"; then
  echo "[deploy] retire legacy compose project 'docker' (containers only; volumes kept)"
  docker compose -p docker -f docker/docker-compose.prod.yml --env-file docker/.env --env-file "$DEPLOY_ENV_FILE" down
fi

echo "[deploy] docker compose up (no build)"
"${COMPOSE[@]}" up -d --no-build --remove-orphans

echo "[deploy] prune dangling images (free disk on the VPS)"
docker image prune -f >/dev/null

echo "[deploy] wait for api health (via gateway — direct api port closed in 025 cutover)"
deadline=$((SECONDS + 120))
until curl -sf http://127.0.0.1:8080/api/v1/health >/dev/null 2>&1; do
  if [[ $SECONDS -gt $deadline ]]; then
    api_logs="$("${COMPOSE[@]}" logs api 2>&1 || true)"
    if grep -q StartupMigrationException <<<"$api_logs"; then
      echo "error: STARTUP MIGRATION FAILURE — the api refused to start rather than serve a half-migrated schema" >&2
      echo "error: see docs/OPERATIONS_RUNBOOK.md §8 (Startup Migration Failure)" >&2
      grep "STARTUP MIGRATION FAILURE" <<<"$api_logs" | tail -n 1 >&2 || true
      exit 1
    fi
    echo "error: api health check timed out after 120s" >&2
    "${COMPOSE[@]}" logs --tail 60 api
    exit 1
  fi
  sleep 2
done

echo "[deploy] ok — api reachable via gateway on 127.0.0.1:8080"

# The gateway's passive health check marks the api destination unhealthy when a request meets
# "Connection refused" during the api's startup, and keeps it so for the cluster's
# ReactivationPeriod (1 minute, appsettings.json). The health wait above still passes (YARP
# forwards when every destination is unhealthy), but /gateway/ready — the contract's ready item
# for the gateway — answers 503 for that whole minute, longer than the contract's retries span.
# So wait for it here, with a budget well past the reactivation period, before the contract runs.
GATEWAY_READY_BUDGET_SECONDS=180
echo "[deploy] wait for gateway ready (every cluster has an available destination)"
deadline=$((SECONDS + GATEWAY_READY_BUDGET_SECONDS))
until curl -sf http://127.0.0.1:8080/gateway/ready >/dev/null 2>&1; do
  if [[ $SECONDS -gt $deadline ]]; then
    echo "error: gateway readiness timed out after ${GATEWAY_READY_BUDGET_SECONDS}s" >&2
    curl -s http://127.0.0.1:8080/gateway/ready >&2 || true
    "${COMPOSE[@]}" logs --tail 60 gateway
    exit 1
  fi
  sleep 2
done
echo "[deploy] ok — gateway ready"

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
