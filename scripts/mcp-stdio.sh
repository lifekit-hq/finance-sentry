#!/usr/bin/env bash
# Finance Sentry MCP over stdio, for a desktop MCP client on another machine (Claude Desktop runs
# `ssh <box> mcp-stdio.sh`). Copy this script onto the box that runs docker-compose.prod.yml.
#
# Each session is a throwaway container from the deployed finance-sentry-mcp image, on its compose network,
# with its environment, in stdio mode - a redeploy needs nothing here. The MCP OAuth credentials (a rotating
# refresh token) live on the host in $CRED_DIR so they outlive the container.
#
#   mcp-stdio.sh login    one-time browser sign-in; forward the printed callback port from the machine
#                         with the browser first (ssh -L <port>:127.0.0.1:<port> <box>)
#   mcp-stdio.sh          serve MCP on stdin/stdout
#   mcp-stdio.sh status | logout
set -euo pipefail

SRC=${FINANCE_SENTRY_MCP_CONTAINER:-finance-sentry-mcp}
NETWORK=${FINANCE_SENTRY_NETWORK:-finance-sentry}
CRED_DIR=${FINANCE_SENTRY_MCP_CRED_DIR:-$HOME/.config/finance-sentry-mcp}

mkdir -p "$CRED_DIR"
chmod 700 "$CRED_DIR"

image=$(docker inspect -f '{{.Config.Image}}' "$SRC")
envfile=$(mktemp)
trap 'rm -f "$envfile"' EXIT
docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$SRC" \
  | grep -v -E '^(MCP_TRANSPORT|MCP_HTTP_PORT|PATH|HOME)=' | grep -v '^$' > "$envfile"
internal_api=$(sed -n 's/^Mcp__ApiBaseUrl=//p' "$envfile")
public_base=$(sed -n 's/^Mcp__FrontendBaseUrl=//p' "$envfile")

run() {
  docker run --rm --user "$(id -u):$(id -g)" \
    --env-file "$envfile" \
    -e MCP_TRANSPORT=stdio -e HOME=/tmp \
    -e FINANCE_SENTRY_MCP_AUTH_FILE=/creds/mcp-auth.json \
    -v "$CRED_DIR:/creds" \
    "$@"
}

case "${1:-serve}" in
  serve)
    run -i --network "$NETWORK" "$image" ;;
  login)
    # Host network so the CLI's 127.0.0.1 callback listener is reachable through an ssh -L tunnel;
    # the compose service names it still needs are pinned to their bridge addresses.
    hosts=()
    for svc in postgres api; do
      ip=$(docker inspect -f "{{(index .NetworkSettings.Networks \"$NETWORK\").IPAddress}}" "finance-sentry-$svc")
      hosts+=(--add-host "$svc:$ip")
    done
    run -it --name finance-sentry-mcp-login --network host "${hosts[@]}" "$image" auth login \
      --api-base "$public_base/api/v1" --frontend-base "$public_base"
    # Refresh from inside the compose network, not through the public gateway.
    tmp=$(mktemp)
    jq --arg u "$internal_api" '.apiBaseUrl = $u' "$CRED_DIR/mcp-auth.json" > "$tmp"
    mv "$tmp" "$CRED_DIR/mcp-auth.json"
    chmod 600 "$CRED_DIR/mcp-auth.json" ;;
  status|logout)
    run --network "$NETWORK" "$image" auth "$1" ;;
  *)
    echo "usage: $0 [serve|login|status|logout]" >&2; exit 2 ;;
esac
