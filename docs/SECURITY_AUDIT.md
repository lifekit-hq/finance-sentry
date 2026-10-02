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
| JWT validation on all endpoints | ✅ | Stock JwtBearer + a fallback policy requiring an authenticated user; validates signature, expiry and `aud=app`, and rejects tokens whose account is missing or locked out |
| JWT exempt paths audited | ✅ | Anonymous endpoints are exactly the `[AllowAnonymous]` set pinned by `ApiAuthenticationPipelineTests` (`/hangfire` maps with the `RequireOwner` policy) |
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
| CSP headers set | ✅ | CSP, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` on every frontend (`docker/nginx.security-headers.conf`) and gateway (`Gateway:SecurityHeaders`) response; `frontend/e2e/security-headers.spec.ts` runs the app under the policy |

### Rate Limiting & DoS

| Check | Status | Evidence |
|-------|--------|----------|
| Anonymous rate limit | ✅ | 10 req/min per client address on the anonymous auth endpoints (`RateLimitPartitions.Anonymous`, attached via `[EnableRateLimiting]`); tunable `RateLimiting:Anonymous:PermitPerMinute` |
| Authenticated rate limit | ✅ | 100 req/min per user (client address when unauthenticated), the default on every controller action (`Program.cs`); health probe exempt; tunable `RateLimiting:Authenticated:PermitPerMinute`. Address comes from forwarded headers trusted only from `ForwardedHeaders:KnownProxies` |

### Input Validation

| Check | Status | Evidence |
|-------|--------|----------|
| publicToken validated | ✅ | `ValidPublicTokenAttribute`: required, ≤100 chars |
| Date range validated | ✅ | `ValidDateNotFutureAttribute`, `ValidEndDateAttribute` |
| Pagination bounds | ✅ | `PaginationExtensions`: offset≥0, limit 1–100 |

## Known Accepted Risks

| Risk | Mitigation |
|------|------------|
| Hangfire dashboard access | Signed-in account holding `ops.admin` (Owner role) only, in every environment including Development (`RequireOwner` policy via `MapHangfireDashboardWithAuthorizationPolicy`) |

## Next Review Date

Quarterly — next: 2026-06-29
