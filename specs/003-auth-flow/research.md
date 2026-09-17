# Research: Auth Flow (003-auth-flow)

**Date**: 2026-04-11  
**Purpose**: Resolve all unknowns before plan design

---

## 1. Existing Auth Infrastructure Audit

### What exists
- `JwtAuthenticationMiddleware` validates HS256 tokens from `Authorization: Bearer` header
- JWT secret sourced from env var `JWT_SECRET` via `Jwt:Secret` appsettings key
- Token expiry: 60 minutes (configured in `appsettings.Development.json`)
- Exempt paths (JWT not required): `/health`, `/api/v1/health`, `/swagger`, `/api/webhook/**`, `/hangfire`
- Middleware is already wired in `Program.cs`

### What does NOT exist
- No `/auth/login` or `/auth/register` endpoints
- No User entity or Users table (only `Guid UserId` on `BankAccount` with comment "stored separately, not in scope")
- No JWT token generation service
- No Angular auth module, login page, register page
- No Angular HTTP interceptor
- No Angular route guards
- All BankSync controllers read `userId` from `[FromQuery] Guid userId` — NOT from JWT claims

---

## 2. Decision: User Management Framework

**Decision**: Use **ASP.NET Core Identity** (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`) with a custom `ApplicationUser : IdentityUser` and `AuthDbContext : IdentityDbContext<ApplicationUser>`

**Rationale**: Identity provides battle-tested user management out of the box: password hashing (PBKDF2-HMAC-SHA256), `UserManager<ApplicationUser>` for create/find/check-password, validation pipelines, and future extensibility (roles, claims, lockout). No need to hand-roll password hashing, duplicate email checks, or validation logic.

**What Identity provides in scope**:
- Password hashing (PBKDF2 via `IPasswordHasher<ApplicationUser>`)
- `UserManager.CreateAsync(user, password)` for registration with validation
- `UserManager.CheckPasswordAsync(user, password)` for login credential verification
- `UserManager.FindByEmailAsync(email)` for user lookup
- `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens` schema via migration

**What remains custom**:
- JWT token generation (`JwtTokenService`) — Identity's built-in token provider generates opaque tokens, not JWTs; the existing `JwtAuthenticationMiddleware` is HS256-based and stays in place
- No cookie authentication, no session — tokens only

**Alternative considered**: Custom `IPasswordHasher<User>` — rejected; Identity is more robust and aligns with the plan to possibly add roles/lockout later.

---

## 3. Decision: Auth Module Structure

**Decision**: New `FinanceSentry.Modules.Auth` project following the same pattern as `FinanceSentry.Modules.BankSync`

**Rationale**: Constitution Principle I mandates modular monolith with clear module boundaries. Auth is a distinct domain (user identity management) separate from bank data sync.

**Alternative considered**: Embedding auth in the API project (`FinanceSentry.API`) — rejected because it would create a mixed-concern API project and violate module isolation.

---

## 4. Decision: DbContext for Auth Module

**Decision**: Separate `AuthDbContext : IdentityDbContext<ApplicationUser>` in the Auth module, targeting the same PostgreSQL database, with its own EF Core migrations (migration prefix `M005_*`)

**Rationale**: Module isolation (Principle I). Each module owns its schema. `IdentityDbContext` requires its own context to hold Identity table mappings. Both contexts connect to the same PostgreSQL DB (single deployment unit), but migrations are independently managed per module.

**Alternative considered**: Extending `BankSyncDbContext` — rejected because it couples two unrelated domains, and `IdentityDbContext<T>` inheritance is incompatible with domain-agnostic base contexts.

---

## 5. Decision: JWT Token Generation Location

**Decision**: New `JwtTokenService` (implementing `ITokenService`) inside `FinanceSentry.Modules.Auth/Infrastructure/Services/`. Reads `Jwt:Secret` and `Jwt:ExpiryMinutes` from configuration.

**Rationale**: Token generation is an auth concern, not a validation concern. The existing middleware only validates. Separating generation into the Auth module keeps responsibilities clean.

---

## 6. Decision: Breaking Change — userId from Claims, not Query Params

**Decision**: All BankSync controllers must be updated to extract `userId` from JWT claims (`HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value`) instead of `[FromQuery] Guid userId`

**Rationale**: The auth feature is only complete when the API actually uses the authenticated user's identity. Continuing to accept userId as a query param would be a security hole — any caller could impersonate any user. This change ships in the same feature branch.

**Scope of change**: `AccountsController`, plus any other BankSync controllers that accept `[FromQuery] Guid userId`. The contract changes (query param removed), requiring contract test updates.

---

## 7. Decision: Angular Interceptor Style

**Decision**: Function-based HTTP interceptor using `HttpInterceptorFn` + `withInterceptors([authInterceptor])` in `app.config.ts`

**Rationale**: Angular 20 with standalone components uses function-based interceptors. Class-based interceptors require `HTTP_INTERCEPTORS` token which is the legacy approach. Function-based is idiomatic for the project's existing standalone architecture.

---

## 8. Decision: Angular Route Guards Style

**Decision**: Function-based route guard using `CanActivateFn`

**Rationale**: Same reasoning as interceptors — function-based guards are the Angular 20 idiomatic approach with standalone routing.

---

## 9. Decision: Token Storage

**Decision**: `localStorage` under key `fs_auth_token` (already decided in spec Notes)

**Rationale**: Session survives browser restarts — critical for a daily-use personal finance app. XSS risk acceptable for a non-public single-user app.

**Fallback**: If localStorage is unavailable (private browsing restriction), the session is in-memory for that tab. No silent fallback needed.

---

## 10. Decision: Logout Mechanism

**Decision**: Client-side only — remove token from localStorage, redirect to `/login`. No server-side token revocation.

**Rationale**: Stateless JWT design. Token expiry (60 min) limits the window. Single-user app with no multi-device session concern. Server-side revocation (denylist) is overkill here.

---

## 11. Decision: Auth Response DTO

**Decision**: Auth endpoints return `{ token: string, expiresAt: ISO8601 string, userId: string (GUID) }` for both login and register

**Rationale**: Frontend needs the token and expiry to manage session state. Including `userId` avoids needing a separate `/me` endpoint.

---

## 12. Decision: Claims in JWT

**Decision**: Token includes `sub` (userId as Guid string) and `email` claims. Standard `exp` claim set to expiry.

**Rationale**: Backend middleware and controllers need userId to scope queries. Email is useful for display in future features. No other claims needed.

---

## 13. Risk: Mid-Session Token Expiry

**Finding**: The existing JWT middleware returns 401 on expired tokens. The Angular HTTP interceptor will receive this 401. Since refresh tokens are out of scope, the interceptor should detect 401 responses and redirect to `/login`.

**Decision**: Angular interceptor handles 401 responses by clearing the stored token and redirecting to `/login`. This covers mid-session expiry transparently.
