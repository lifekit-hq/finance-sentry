# Security Audit — Bank Sync Feature

## Penetration Test Checklist

### Injection

| Vector | Status | Evidence |
|--------|--------|----------|
| SQL Injection | ✅ Mitigated | EF Core parameterized queries; no raw SQL string interpolation |
| NoSQL Injection | N/A | No MongoDB/Redis used |
| Command Injection | ✅ Mitigated | No shell commands invoked from user input |

### Authentication & Authorization

| Check | Status | Evidence |
|-------|--------|----------|
| JWT validation on all endpoints | ✅ | `JwtAuthenticationMiddleware` validates signature + expiry |
| JWT exempt paths audited | ✅ | Only `/health`, `/swagger`, `/hangfire`, `/api/v1/auth`, TrueLayer callback |
| TrueLayer callback bound to initiating browser | ✅ | `BankSyncController.TrueLayerCallback` finalizes only when the `fs_truelayer_state` cookie (HttpOnly, SameSite=Lax, 15 min, set by the connect endpoint) equals the OAuth `state`; otherwise redirects with `TRUELAYER_STATE_MISMATCH` |
| FR-009 user scoping | ✅ | All data endpoints verify `account.UserId == requestingUserId` |
| Webhook HMAC-SHA256 | ✅ | `WebhookSignatureValidator` constant-time comparison |

### Sensitive Data Exposure

| Check | Status | Evidence |
|-------|--------|----------|
| Credentials encrypted at rest | ✅ | AES-256-GCM, key never in DB (`EncryptedCredential` stores cipher only) |
| No tokens in logs | ✅ | Code review: no `LogInformation`/`LogError` calls with token fields |
| No stack traces to client | ✅ | `ErrorHandlingMiddleware` logs server-side, returns sanitized message |
| Audit logs contain no PII | ✅ | `AuditLog` stores action + resource ID only; no amounts/descriptions |

### Transport Security

| Check | Status | Evidence |
|-------|--------|----------|
| HTTPS enforced in production | ✅ | `UseHttpsRedirection()` in `Program.cs` |
| CORS restricted | ✅ | Origin whitelist: `localhost:4200` (dev), `finance-sentry.com` (prod) |
| CSP headers set | ❌ | No CSP header today (`index.html` documents the decision) |

### Rate Limiting & DoS

| Check | Status | Evidence |
|-------|--------|----------|
| Anonymous rate limit | ✅ | 10 req/min (ASP.NET Core fixed-window limiter) |
| Authenticated rate limit | ✅ | 100 req/min per user |

### Input Validation

| Check | Status | Evidence |
|-------|--------|----------|
| publicToken validated | ✅ | `ValidPublicTokenAttribute`: required, ≤100 chars |
| Date range validated | ✅ | `ValidDateNotFutureAttribute`, `ValidEndDateAttribute` |
| Pagination bounds | ✅ | `PaginationExtensions`: offset≥0, limit 1–100 |

## Known Accepted Risks

| Risk | Mitigation |
|------|------------|
| Hangfire dashboard access | Protected by JWT middleware; restrict to admin role in production |

## Next Review Date

Quarterly — next: 2026-06-29
