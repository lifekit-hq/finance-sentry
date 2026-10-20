#!/bin/sh
# Ledger's monthly correction rate, read-only: the owner's messages to Ledger in one calendar month (UTC)
# against the corrections Ledger recorded in finance-sentry's correction log (record_ledger_correction) that month.
# The bar is a rate below 10%. Run on the VPS:
#   scripts/ledger-corrections-month.sh [YYYY-MM]    (default: the current month)
# Owner messages: user turns in Ledger's direct Telegram sessions, from the gateway's session store opened
# read-only; relays Kit forwards in ("[Inter-session message]") are not his messages. Corrections: rows of
# companion.ledger_corrections, read in a read-only transaction. Neither store is written.
set -eu

month=${1:-$(date -u +%Y-%m)}
case $month in
  [0-9][0-9][0-9][0-9]-[0-1][0-9]) ;;
  *) echo "usage: $0 [YYYY-MM]" >&2; exit 2 ;;
esac

gateway=${OPENCLAW_GATEWAY_CONTAINER:-openclaw-openclaw-gateway-1}
postgres=${FINANCE_SENTRY_POSTGRES_CONTAINER:-finance-sentry-postgres}

messages=$(docker exec -e MONTH="$month" "$gateway" node -e '
const { DatabaseSync } = require("node:sqlite");
const zlib = require("node:zlib");
const db = new DatabaseSync(process.env.HOME + "/.openclaw/agents/finance/agent/openclaw-agent.sqlite", { readOnly: true });
const from = Date.parse(process.env.MONTH + "-01T00:00:00Z");
const next = new Date(from); next.setUTCMonth(next.getUTCMonth() + 1);
const rows = db.prepare(`
  select e.event_json, e.event_zstd from transcript_events e
  join session_windows w on w.session_id = e.session_id
  where w.session_key like ? and e.created_at >= ? and e.created_at < ?`)
  .all("agent:finance:telegram:%direct:%", from, next.getTime());
const text = c => typeof c === "string" ? c : Array.isArray(c) ? c.map(p => p?.text ?? "").join("") : "";
let n = 0;
for (const r of rows) {
  const json = r.event_json ?? zlib.zstdDecompressSync(Buffer.from(r.event_zstd)).toString("utf8");
  const ev = JSON.parse(json);
  if (ev.type !== "message" || ev.message?.role !== "user") continue;
  if (text(ev.message.content).trimStart().startsWith("[Inter-session message]")) continue;
  n++;
}
console.log(n);
')

psql_ro() {
  docker exec -i -e PGOPTIONS='-c default_transaction_read_only=on -c TimeZone=UTC' "$postgres" \
    psql -U finance_user -d finance_sentry -At -v ON_ERROR_STOP=1 -v month="$month" "$@"
}
# Zero until the migration that adds the log has run.
corrections=0
if [ "$(echo "select to_regclass('companion.ledger_corrections') is not null;" | psql_ro)" = t ]; then
  corrections=$(psql_ro <<'SQL'
select count(*) from companion.ledger_corrections
where "CorrectedAt" >= (:'month' || '-01')::timestamptz
  and "CorrectedAt" < (:'month' || '-01')::timestamptz + interval '1 month';
SQL
)
fi

if [ "$messages" -gt 0 ]; then
  rate=$(awk -v c="$corrections" -v m="$messages" 'BEGIN { printf "%.1f%%", 100 * c / m }')
else
  rate="n/a"
fi
echo "month=$month owner_messages=$messages corrections=$corrections rate=$rate"
