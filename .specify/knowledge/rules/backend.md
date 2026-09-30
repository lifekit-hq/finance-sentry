# Backend Rules

## BE-001 — External integrations must go through domain interfaces
**Category**: backend
**Source**: constitution § I
**Added**: 2026-04-18

Every external service (bank APIs, broker APIs, crypto exchanges) MUST be accessed via a
domain-defined interface (e.g., `IBankProvider`, `IBrokerAdapter`). No module may reference
a concrete adapter directly.

```csharp
// WRONG
public class SyncService(TrueLayerHttpClient client) { }

// CORRECT
public class SyncService(IBankProvider bankProvider) { }
```

Concrete implementations are registered in Infrastructure and resolved via DI.

---

## BE-002 — No cross-module direct coupling
**Category**: backend
**Source**: constitution § I
**Added**: 2026-04-18

Modules communicate through well-defined service boundaries only.
Never import types from another module's `Infrastructure` or `Domain` namespace directly.
Use shared contracts or published events.

---

## BE-003 — Each integration module is self-contained
**Category**: backend
**Source**: constitution § III
**Added**: 2026-04-18

Each integration module must own its:
- Data synchronization logic
- Error handling
- Retry logic

No shared retry/sync helpers across modules unless extracted to a shared kernel.

---

## BE-004 — Financial data encrypted at rest and in transit
**Category**: backend, security
**Source**: constitution § V
**Added**: 2026-04-18

All sensitive financial fields must be encrypted at rest (AES-256 or equivalent).
All API communication must be TLS-only. No plaintext financial data in logs or DB.

---

## BE-005 — Auth enforced at API boundary and per-module
**Category**: backend, security
**Source**: constitution § V
**Added**: 2026-04-18

Authentication is the stock JwtBearer handler plus a fallback policy that requires an authenticated
user, so every endpoint is protected unless it carries `[AllowAnonymous]` (health, metrics, the auth
sign-in endpoints, the TrueLayer callback). `ApiAuthenticationPipelineTests` pins that anonymous list;
adding to it is a reviewed change. `/hangfire` is mapped with the `RequireOwner` policy.

---

## BE-006 — All queries and reports must be user-scoped
**Category**: backend, security
**Source**: constitution § V
**Added**: 2026-04-18

User data isolation is absolute. Every DB query, cache key, and report must be filtered
by the authenticated user's ID. Never return data across user boundaries.

---

## BE-007 — Secrets never logged
**Category**: backend, security
**Source**: constitution § V
**Added**: 2026-04-18

API keys, tokens, passwords, and account numbers must never appear in logs.
Use structured logging with explicit property exclusions for sensitive fields.
