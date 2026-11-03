# API Contract: Monobank Connect

**Feature**: `007-monobank-adapter`  
**Controller**: `BankSyncController` (extended)

---

## POST /api/v1/accounts/monobank/connect

Validates a Monobank personal API token, fetches all accounts (cards) associated with it, initiates a 90-day transaction history import, and returns the list of created accounts.

### Request

```http
POST /api/v1/accounts/monobank/connect
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "token": "uXXXXXXXXXXXXXXXXXXXXXXX"
}
```

| Field | Type | Required | Validation |
|---|---|---|---|
| `token` | string | yes | Non-empty; max 64 chars |

### Response — 201 Created

```json
{
  "accounts": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "bankName": "Monobank",
      "accountType": "checking",
      "accountNumberLast4": "1234",
      "ownerName": "Alex Morgan",
      "currency": "UAH",
      "currentBalance": 15234.50,
      "syncStatus": "pending",
      "provider": "monobank"
    }
  ]
}
```

### Response — 400 Bad Request (invalid/expired token)

```json
{
  "error": "Invalid or expired Monobank token.",
  "errorCode": "MONOBANK_TOKEN_INVALID"
}
```

### Response — 409 Conflict (token already connected)

```json
{
  "error": "This Monobank token is already connected.",
  "errorCode": "MONOBANK_TOKEN_DUPLICATE"
}
```

### Response — 401 Unauthorized

Standard JWT auth failure — no body.

### Response — 429 Too Many Requests

Monobank rate limit hit during token validation. Retry after 60 seconds.

```json
{
  "error": "Monobank API rate limit exceeded. Please try again in 60 seconds.",
  "errorCode": "MONOBANK_RATE_LIMITED"
}
```

---

## GET /api/v1/accounts (existing, extended)

No contract change. The response now includes accounts with `"provider": "monobank"` in addition to `"provider": "plaid"`. Existing consumers that ignore unknown fields are unaffected.

**Response shape addition** (per account object):

```json
{
  "provider": "plaid" | "monobank"
}
```

---

## DELETE /api/v1/accounts/{accountId} (existing, extended)

No contract change. When deleting a Monobank account, the system:
1. Soft-deletes the `BankAccount` row.
2. If no other accounts reference the same `MonobankCredential`, the credential row is also deleted.

The response is unchanged (`204 No Content`).
