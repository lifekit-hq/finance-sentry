#!/usr/bin/env bash
# Probe the running FinanceSentry MCP stdio server over a fresh exec session.
#
# Usage:
#   ./mcp-probe.sh                     # list all tools
#   ./mcp-probe.sh <tool> <json-args>  # call one tool with arguments
#
# Examples:
#   ./mcp-probe.sh
#   ./mcp-probe.sh get_sync_health '{}'
#   ./mcp-probe.sh list_transactions '{"pageSize":5}'
#
# Requires the finance-sentry-mcp container to be running.

set -euo pipefail

CONTAINER="${MCP_CONTAINER:-finance-sentry-mcp}"

if ! docker ps --format '{{.Names}}' | grep -q "^${CONTAINER}\$"; then
  echo "error: container '${CONTAINER}' is not running. Start it with: cd docker && docker compose -f docker-compose.dev.yml up -d mcp" >&2
  exit 1
fi

init='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"mcp-probe","version":"1"}}}'
notif='{"jsonrpc":"2.0","method":"notifications/initialized"}'

if [[ $# -eq 0 ]]; then
  request='{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
else
  tool="$1"
  args="${2:-{\}}"
  request="{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"${tool}\",\"arguments\":${args}}}"
fi

tmp_out=$(mktemp)
trap 'rm -f "${tmp_out}"' EXIT

{
  printf '%s\n' "${init}" "${notif}" "${request}"
  sleep 5
} | docker exec -i "${CONTAINER}" dotnet FinanceSentry.Mcp.dll 2>/dev/null > "${tmp_out}" &
pid=$!
sleep 15
kill "${pid}" 2>/dev/null || true
wait "${pid}" 2>/dev/null || true

python3 - "${tmp_out}" <<'PY'
import json, sys
with open(sys.argv[1]) as f:
    data = f.read()
for line in data.splitlines():
    line = line.strip()
    if not line.startswith('{'):
        continue
    try:
        obj = json.loads(line)
    except json.JSONDecodeError:
        continue
    if obj.get('id') == 2:
        print(json.dumps(obj, indent=2))
        sys.exit(0)
print("no response found for id=2", file=sys.stderr)
print("--- raw stdout ---", file=sys.stderr)
print(data, file=sys.stderr)
sys.exit(1)
PY
