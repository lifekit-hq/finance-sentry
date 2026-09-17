# Contract: Gateway Routing Table & Policies

The gateway exposes **no new business endpoints** — it proxies existing services. This contract
pins the routing table, the policy bindings, and the observable behaviours the acceptance
scenarios test.

## Routing table

| # | Inbound path | Method(s) | → Cluster | Upstream address | Transform | Rate policy |
|---|---|---|---|---|---|---|
| 1 | `/api/v1/auth/{**}` | any | `api` | `http://api:5000` | none | `auth` |
| 2 | `/api/webhook/{**}` | any | `api` | `http://api:5000` | none | `webhook` |
| 3 | `/api/{**}` | any | `api` | `http://api:5000` | none | — |
| 4 | `/hangfire/{**}` | any | `api` | `http://api:5000` | none | — |
| 5 | `/mcp/{**}` | any | `mcp` | `http://mcp:5100` | strip `/mcp` | — |
| 6 | `/{**}` (fallback) | any | `frontend` | `http://frontend:4200` | none | — |

Route ordering: more specific auth/webhook routes MUST match before the generic `/api/{**}` route;
the `/{**}` fallback MUST be last.

## Gateway-local endpoints (not proxied)

| Path | Purpose |
|---|---|
| `/metrics` | Prometheus exposition (FR-007) — request counts, proxy latency, throttle counts. |
| `/gateway/health` | Gateway liveness (so compose/monitoring can watch the SPOF). |
| `/gateway/ready` | Gateway readiness (added by spec 048) — 200 while every cluster has an available destination, 503 naming the empty one. |

> `/metrics` and `/gateway/*` are namespaced so they never collide with a proxied path.

## Behavioural contract (maps to acceptance scenarios)

| ID | Given / When | Then |
|---|---|---|
| B1 (US1-1) | Browser GET gateway origin `/` | SPA served; its `/api/*` XHR routed to API; response identical to direct (headers, cookies, auth intact). |
| B2 (US1-2) | GET `/api/v1/...` valid | Proxied to API; status/headers/body identical to hitting `api:5000` directly. |
| B3 (US1-4) | GET unknown `/api/does-not-exist` | API's own clean 404 (no topology leak). |
| B4 (US2-1) | Plain HTTP request when TLS enabled | 308/301 redirect to HTTPS. |
| B5 (US2-2) | Cert nearing expiry (TLS enabled) | LettuceEncrypt renews with no operator action, no downtime. |
| B6 (US3-1) | > limit requests to `/api/v1/auth/login` from one IP | 429 for the excess; throttle visible in `/metrics`. |
| B7 (US3-2) | Normal login rate | No throttling. |
| B8 (US4-1) | `api` container stopped | Gateway returns fast **503** (< 2 s), not a timeout. |
| B9 (US4-2) | `api` recovers, health probe passes | Routing resumes automatically. |
| B10 (edge) | MCP streamable-HTTP / WebSocket upgrade via `/mcp` | Proxied correctly (upgrade + streaming preserved). |
| B11 (edge) | Large export download via `/api/...` | Streamed, not truncated. |
| B12 (FR-006) | Any proxied request | Backend sees real client IP via `X-Forwarded-For`; Serilog logs the real IP. |

## Non-goals (explicit)

- Gateway does **not** validate JWT (stays in the API — spec OUT OF SCOPE).
- Gateway does **not** serve static assets itself (nginx keeps that job).
- Gateway does **not** rewrite response bodies.
