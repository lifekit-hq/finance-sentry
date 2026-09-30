# API Contracts: Auth Flow (003-auth-flow)

**Date**: 2026-04-11  
**Base URL**: `http://localhost:5000/api/v1`  
**Auth**: Endpoints in this module are exempt from JWT authentication (no Bearer token required for login/register)

---

## POST /auth/register

Register a new user account. On success, returns a JWT ready for immediate use — no separate login step required.

### Request

```
POST /api/v1/auth/register
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "SecurePass123!"
}
```

| Field | Type | Required | Validation |
|-------|------|----------|------------|
| email | string | yes | Valid email format; must be unique |
| password | string | yes | Min 8 chars; must not be a common password (no composition rules) |

### Response — 201 Created

```json
{
  "token": "<HS256 JWT>",
  "expiresAt": "2026-04-11T14:00:00Z",
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

### Response — 400 Bad Request (validation failure)

```json
{
  "error": "Validation failed.",
  "errorCode": "VALIDATION_ERROR",
  "details": [
    "Email is already registered.",
    "Password must be at least 8 characters."
  ]
}
```

### Response — 400 Bad Request (duplicate email)

```json
{
  "error": "Email is already registered.",
  "errorCode": "DUPLICATE_EMAIL"
}
```

---

## POST /auth/login

Authenticate an existing user. Returns a JWT on success.

### Request

```
POST /api/v1/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "SecurePass123!"
}
```

| Field | Type | Required | Validation |
|-------|------|----------|------------|
| email | string | yes | Non-empty |
| password | string | yes | Non-empty |

### Response — 200 OK

```json
{
  "token": "<HS256 JWT>",
  "expiresAt": "2026-04-11T14:00:00Z",
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

### Response — 401 Unauthorized (wrong credentials)

```json
{
  "error": "Invalid email or password.",
  "errorCode": "INVALID_CREDENTIALS"
}
```

*Note: Deliberately generic — does not reveal whether the email exists.*

### Response — 400 Bad Request (missing fields)

```json
{
  "error": "Validation failed.",
  "errorCode": "VALIDATION_ERROR",
  "details": ["Email is required.", "Password is required."]
}
```

---

## Changes to Existing Endpoints (Breaking)

The following existing BankSync endpoints currently accept `?userId=<guid>` as a query parameter. After this feature, `userId` is derived exclusively from the JWT claim `sub`. The query parameter is removed.

| Endpoint | Change |
|----------|--------|
| GET /api/v1/accounts | Remove `?userId` query param; userId from JWT claim `sub` |
| POST /api/v1/accounts/connect | Remove `?userId` query param (if present); userId from claim |
| POST /api/v1/accounts/link | Remove `?userId` query param; userId from claim |
| GET /api/v1/accounts/{id}/transactions | Remove `?userId` query param; userId from claim |
| POST /api/v1/accounts/{id}/sync | Remove `?userId` query param; userId from claim |
| GET /api/v1/accounts/{id}/sync-status | Remove `?userId` query param; userId from claim |
| DELETE /api/v1/accounts/{id} | Remove `?userId` query param; userId from claim |
| GET /api/v1/dashboard/aggregated | Remove `?userId` query param (if present); userId from claim |

**Error response for unauthenticated requests** (unchanged — already handled by `JwtAuthenticationMiddleware`):

```json
{
  "error": "Authentication required.",
  "errorCode": "UNAUTHORIZED"
}
```

**HTTP Status**: 401

---

## JWT Token Format

```
Header:  { "alg": "HS256", "typ": "JWT" }
Payload: {
  "sub": "<ApplicationUser.Id GUID>",
  "email": "<user email>",
  "exp": <unix timestamp>,
  "iat": <unix timestamp>
}
Signature: HMAC-SHA256(secret from JWT_SECRET env var)
```

---

## Contract Test Requirements (per Constitution v1.1.1)

Each endpoint above requires a contract test in the same PR that introduces or modifies it:

| Test | Validates |
|------|-----------|
| `POST /auth/register` — happy path | 201 status; response schema matches `AuthResponse` |
| `POST /auth/register` — duplicate email | 400 status; `DUPLICATE_EMAIL` errorCode |
| `POST /auth/register` — missing fields | 400 status; `VALIDATION_ERROR` errorCode |
| `POST /auth/login` — happy path | 200 status; response schema matches `AuthResponse` |
| `POST /auth/login` — wrong password | 401 status; `INVALID_CREDENTIALS` errorCode |
| `POST /auth/login` — missing fields | 400 status; `VALIDATION_ERROR` errorCode |
| `GET /accounts` — no token | 401 status; `UNAUTHORIZED` errorCode |
| `GET /accounts` — valid token | 200 status; userId extracted from claim (no query param) |
