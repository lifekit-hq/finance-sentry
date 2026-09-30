# MCP Tool Contracts: Surface Refinement (before → after)

All tools continue to resolve the caller via `IIdentityResolver`. (Superseded: the optional `userId` override described below has since been removed from every tool — see `docs/mcp.md` § Identity.)

## Merged: `watchlist` (replaces 3 tools)

**Removes**: `list_watchlist`, `add_to_watchlist`, `remove_from_watchlist`.

**Request**:
| Param | Type | Required | Notes |
|---|---|---|---|
| `action` | string | yes | `list` \| `add` \| `remove` |
| `ticker` | string | for `add` | e.g. AAPL, NVDA, BTC-USD |
| `exchange` | string | no (`add`) | e.g. NASDAQ |
| `note` | string | no (`add`) | why it's tracked |
| `itemId` | Guid | for `remove` | id from a prior `list`/`add` |
| `userId` | Guid | no | defaults to MCP identity |

**Response** (`WatchlistToolResult`, null members omitted):
- `list` → `{ "action":"list", "items":[ … ] }`
- `add` → `{ "action":"add", "item":{ … } }`
- `remove` → `{ "action":"remove", "removed":true }`
- invalid → `{ "action":"add", "error":"action=add requires a ticker" }`

**Description** (indicative): "Manage the caller's watchlist (tickers tracked, not necessarily held). `action=list` returns all; `action=add` needs `ticker`; `action=remove` needs `itemId` (from a prior list/add)."

**Capability preservation**: all three prior operations reachable; `remove` still targets by `itemId`; `add` idempotency behavior unchanged (handler still errors on duplicate).

## Enriched: `run_thesis_monitor` (name unchanged)

**Request**: unchanged (`userId?`).

**Response** (was `ThesisMonitorRunSummary`, now `ThesisMonitorResult`):
```json
{ "summary": { /* existing ThesisMonitorRunSummary */ },
  "breaks":  [ /* ThesisBreakView[] after the run */ ] }
```
**Description** (indicative): "Re-evaluates the caller's active theses now (same path as the scheduled job; persists break-state and raises/resolves alerts) AND returns the resulting breaks. For a read-only view that does NOT re-evaluate or fire alerts, use `list_thesis_breaks`."

## Unchanged (retained deliberately)

- `list_thesis_breaks` — pure read, kept (boundary).
- `get_ips`/`save_ips`, `get_risk_rules`/`save_risk_rules` — read/write pairs, kept.
- `get_notification_mode`/`set_notification_mode`, `get_pending_companion_events`/`acknowledge_companion_events` — two n=2 read/write pairs, kept.
- `get_radar_summary` + `get_market_structure`/`get_market_breadth`/`get_relative_strength`/`get_sector_rotation` — entry + drill-downs, kept (descriptions updated for ordering).

## Description-only edits (no signature change)

Entry/follow-up sentences added to: `get_radar_summary` (entry), the four market drill-downs (point back to entry), `promote_candidate` (states the `check_risk_rules` gate), and any other tool whose correct use implies an order currently only documented in agent prose. Enumerated during implement.

## Contract test

`ToolNameContractTests.AgreedToolSurface`: remove the three watchlist names, add `watchlist` → **55**; update the "because" message to 55.
