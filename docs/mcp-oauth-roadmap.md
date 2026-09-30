# Finance Sentry MCP OAuth Roadmap

## Current State

As of this branch:

- `stdio` MCP uses locally stored MCP OAuth credentials.
- HTTP MCP requires per-request `Authorization: Bearer <mcp access token>`.
- Identity is resolved per request for HTTP and from locally refreshed MCP credentials for `stdio`.
- The API exposes MCP-specific authorization, token, and revoke endpoints.

This is now a real MCP-specific OAuth-style flow for first-party clients, though it is **not yet a full general-purpose external OAuth platform**.

## What "Real OAuth MCP" Means

For a remote MCP server, the target model is:

1. The MCP host authenticates the user with Finance Sentry using a standard OAuth flow.
2. The host receives short-lived access tokens scoped for MCP use.
3. The host refreshes those tokens through an OAuth-compatible token flow.
4. The MCP server authorizes each request based on the presented token.

That is materially different from:

- a long-lived env token pasted into `.env`
- a browser cookie intended for the frontend SPA
- a host manually injecting a static bearer token forever

## Reference Ecosystem Patterns

Observed patterns from public MCP servers:

- **GitHub remote MCP**
  - Preferred: host-supported OAuth
  - Fallback: PAT in `Authorization: Bearer ...`
- **GitHub local MCP**
  - Browser OAuth on first use
  - Token kept in memory or refreshed by the local server
  - PAT fallback available
- **Google Drive local MCP**
  - Local browser OAuth flow
  - Refresh-capable credentials stored locally

Finance Sentry should follow the same split:

- `stdio`: local OAuth/device flow or local managed credential
- HTTP: request-based OAuth bearer tokens

## Recommended Target Architecture

### `stdio` transport

Best target:

- Add a local login/bootstrap command for MCP
- Run browser-based login or device-code login
- Store refresh-capable MCP credentials locally
- Mint short-lived MCP access tokens automatically

Fallback:

- Keep a local login helper for headless/dev workflows

### HTTP transport

Best target:

- Add a dedicated OAuth-compatible authorization surface for MCP clients
- Issue short-lived access tokens with an MCP audience/scope
- Support token refresh through a token endpoint
- Require bearer auth on every MCP HTTP request

## Phased Delivery Plan

### Phase 1: Completed

- HTTP MCP uses request-based JWT auth
- Identity comes from `HttpContext.User` for HTTP
- `stdio` works with local OAuth bootstrap and stored refreshable credentials

### Phase 2: Completed

- dedicated short-lived MCP access tokens
- explicit MCP audience validation on HTTP
- frontend SPA cookies removed from MCP HTTP authentication

### Phase 3: Completed for first-party MCP clients

- authorization endpoint
- token endpoint
- refresh token support for MCP clients
- revoke endpoint

### Phase 4: Completed for host-run `stdio`

- `auth login`
- browser callback flow for desktop/dev clients
- local credential storage with automatic refresh

Remaining gap:

- device-code or equivalent headless/container login UX

## Code-Level Next Step

The next engineering step should be:

1. Add a device-code or comparable headless login flow for containerized/local MCP processes
2. Introduce explicit MCP client registration metadata for first-party clients

## Non-Goals For Now

Avoid doing these prematurely:

- dynamic OAuth client registration
- multi-tenant external app ecosystem support
- full OIDC discovery surface
- mixing local `stdio` bootstrap UX with remote HTTP auth concerns in one implementation

## Summary

The repo is now in a better state than before, but the real OAuth destination is:

- short-lived MCP bearer tokens
- refreshable credentials
- per-request authorization
- transport-specific auth behavior
- local login UX for `stdio`
- a headless login path for non-host `stdio` deployments

That should continue from the current MCP OAuth foundation instead of reintroducing static server tokens.
