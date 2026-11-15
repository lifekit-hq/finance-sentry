# Feature Specification: Edge Gateway

**Feature Branch**: `025-edge-gateway`
**Created**: 2026-07-09
**Status**: Implemented
**Input**: User description: "Edge gateway: single reverse-proxy entrypoint in front of frontend, API and MCP with routing, TLS termination, rate limiting and health-based routing"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One front door for the whole system (Priority: P1)

As the operator, all inbound traffic (web app, API, MCP) enters through a single gateway that routes by path/host to the right internal service, so backend containers are no longer individually exposed and the system has one place to control ingress.

**Why this priority**: The gateway is the architectural point of this feature — it decouples "how clients reach the system" from "how services are deployed", which is the prerequisite for replicas, service extraction, and orchestration later.

**Independent Test**: With the gateway in front, reach the SPA, an API endpoint, and the MCP endpoint through the single entrypoint; verify direct access to backend ports from outside the host is no longer possible.

**Acceptance Scenarios**:

1. **Given** the gateway is deployed, **When** a browser requests the app origin, **Then** the SPA is served and its API calls route through the gateway to the backend.
2. **Given** the gateway is deployed, **When** a client hits an API route, **Then** the request is proxied to the API service and the response is identical to direct access (headers, auth, cookies all intact).
3. **Given** the gateway is the only published entrypoint, **When** an external scan probes the previous direct service ports, **Then** they are unreachable.
4. **Given** an unknown path, **When** requested, **Then** the gateway returns a clean 404 without leaking internal topology.

---

### User Story 2 - TLS at the edge (Priority: P1)

As a user, all traffic to the system is HTTPS with a valid, auto-renewing certificate; internal services no longer each deal with TLS.

**Why this priority**: Table stakes for anything financial; also concentrates certificate management in one place.

**Independent Test**: Request the public origin over HTTPS and verify a valid certificate; request over HTTP and verify redirect to HTTPS.

**Acceptance Scenarios**:

1. **Given** the gateway is live, **When** a client connects over HTTP, **Then** it is redirected to HTTPS.
2. **Given** certificate expiry approaches, **When** the renewal window arrives, **Then** the certificate renews without operator action or downtime.

---

### User Story 3 - Abuse protection via rate limiting (Priority: P2)

As the operator, unauthenticated endpoints (login, register, webhooks) are rate-limited per client at the edge, so brute-force and accidental floods are absorbed before reaching the application.

**Why this priority**: Real security value, but the system functions without it; builds on Story 1's routing.

**Independent Test**: Fire requests at the login route beyond the limit; verify throttled responses and that normal traffic is unaffected.

**Acceptance Scenarios**:

1. **Given** a client exceeds the login rate limit, **When** the next request arrives, **Then** it receives a throttle response (429) and the attempt is visible in gateway metrics/logs.
2. **Given** a well-behaved client, **When** using the app normally, **Then** no throttling occurs.

---

### User Story 4 - Health-based routing (Priority: P3)

As the operator, the gateway checks backend health and stops routing to an unhealthy instance, returning a clear maintenance response instead of hanging — and when multiple API replicas exist later, it balances across the healthy ones.

**Why this priority**: With a single instance the benefit is failing fast + readiness for replicas; load balancing across replicas becomes real practice in the orchestration feature.

**Independent Test**: Stop the API container; verify the gateway serves an immediate 503 (not a timeout) and recovers automatically when the API returns.

**Acceptance Scenarios**:

1. **Given** the API is down, **When** a request arrives, **Then** the gateway responds with a fast, clean 503.
2. **Given** the API recovers, **When** its health check passes, **Then** routing resumes without operator action.

### Edge Cases

- WebSocket/SSE and long-lived connections (MCP HTTP transport, dev hot-reload) must proxy correctly.
- Large payloads (data export) must not be truncated by proxy body-size defaults.
- Client IPs must be forwarded so application logs and rate limiting see real addresses, not the proxy's.
- Webhook endpoints (Plaid) must remain reachable by the external provider through the gateway with signature verification unaffected.
- Gateway itself becomes a single point of failure: it must restart automatically and start faster than the services it fronts.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A single gateway MUST be the only published entrypoint on the production host; frontend, API, and MCP MUST be reachable only through it (internal network otherwise).
- **FR-002**: The gateway MUST route by path/host to frontend, API, and MCP services via declarative configuration versioned in the repository.
- **FR-003**: The gateway MUST terminate TLS with automatically renewed certificates and redirect HTTP to HTTPS.
- **FR-004**: The gateway MUST apply per-client rate limits on authentication and webhook endpoints, configurable per route.
- **FR-005**: The gateway MUST health-check backends, fail fast (clean 503) when a backend is down, and support multiple upstream instances per service for future replicas.
- **FR-006**: The gateway MUST forward client identity headers (original IP, protocol) to backends, and backends MUST honor them in logs and auth decisions.
- **FR-007**: The gateway MUST expose its own metrics (request counts, upstream latency, throttle events) to the observability stack.
- **FR-008**: Local development MUST keep working with an equivalent (or bypassed) gateway path so dev/prod parity is reasonable.

### Key Entities

- **Route**: match rule (host/path) → upstream service, with per-route policies (rate limit, body size, timeouts).
- **Upstream**: named backend service with one or more addresses and a health-check definition.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of production traffic flows through the gateway; no backend port is directly reachable from the internet.
- **SC-002**: All existing user flows (login, sync, dashboards, webhooks, MCP queries) work unchanged through the gateway — zero regressions in the QA golden paths.
- **SC-003**: Gateway adds < 10 ms p95 overhead per request.
- **SC-004**: A backend outage yields an error response in < 2 seconds instead of a browser timeout.
- **SC-005**: A brute-force login attempt (100 requests/min from one client) is throttled while a normal user logs in unaffected.

## Assumptions

- Single-host deployment; "load balancing" is configured and testable but has one real upstream until replicas arrive with orchestration.
- The gateway replaces any current per-service port publishing in the production compose file.
- Certificate issuance uses a public CA with automated challenge (the host already has a public domain or will get one; if only an IP exists, a domain is a prerequisite).
- MCP HTTP transport is routed; MCP stdio usage on the host is unaffected.

## Notes

- [DECISION] The gateway is a practice-driven architectural step: its full value (balancing replicas, service extraction) is realized by later features; this feature makes ingress production-shaped now.
- [OUT OF SCOPE] Authentication at the gateway (JWT validation stays in the API); revisit when services multiply.
- [OUT OF SCOPE] CDN/static-asset offloading.
- [DEFERRED] Blue/green or canary routing — becomes meaningful with orchestration (k8s feature).
- [KNOWN LIMITATION] Fast-fail (SC-004, <2s 503 on a down backend) is **not fully met** in the current single-host build. The `api` cluster's long `HttpRequest.ActivityTimeout` (set for Hangfire/MCP long-poll parity) also bounds connect, so a stopped/removed backend hangs through the active-health detection window instead of returning a fast 503. Surfaced by live smoke-testing after merge (PR #383/#384). Accepted as a standing known limitation (the Kubernetes migration it was deferred to, 027, was dropped on 2026-11-15). Fix if it ever bites: give the api cluster a short connect timeout via a custom forwarder `HttpClient` (keeping the long `ActivityTimeout` only where long-poll needs it — e.g. split Hangfire/MCP into their own cluster). Not a daily-usage blocker; steady-state routing and rate limiting are unaffected.
