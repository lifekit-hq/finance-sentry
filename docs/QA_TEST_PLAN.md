# QA Test Plan — Bank Account Sync (Feature 001)

## US1: Connect & View Accounts

| # | Test Case | Steps | Expected Result |
|---|-----------|-------|-----------------|
| 1 | Happy path: link account | Click "Connect Bank", complete the TrueLayer sandbox consent flow | Account appears in list with status "active" |
| 2 | Duplicate account | Link same bank account twice | Second attempt rejected with clear error message |
| 3 | Invalid public token | Submit corrupted `publicToken` | 400 "Bank credentials expired. Please reconnect." |
| 4 | View transactions | Open Transactions, optionally filter by the linked account's chip | Paginated list loads, amounts and dates correct |
| 5 | Delete account | Click "Disconnect" on an account | Account removed from list; 204 returned |
| 6 | User isolation | User A cannot see User B's accounts | 404 returned for cross-user account ID |

## US2: Automatic Sync

| # | Test Case | Steps | Expected Result |
|---|-----------|-------|-----------------|
| 7  | Manual sync trigger | Click "Sync Now" | 202 Accepted, sync status shows "syncing" |
| 8  | Sync idempotency | Trigger sync twice rapidly | Second request returns 409 "sync already running" |
| 9  | Sync completion | Wait for sync to finish | Status changes to "active", transaction count updated |
| 10 | Duplicate deduplication | Sync same transactions twice | No duplicates in transaction list |
| 11 | Provider outage | Provider API returns 503 | Error recorded in SyncJob, account status = "failed"; the next scheduled cycle (15 min) retries - there is no inline retry |
| 12 | Credential expiry | TrueLayer consent expires (invalid_grant) | Account status = "reauth_required", user prompted; MCP `get_sync_health` reports TrueLayer as "error" (also for an EXPIRED connection) |
| 13 | Webhook TRANSACTIONS_READY | POST webhook with valid HMAC | Sync enqueued for affected account |
| 14 | Webhook invalid HMAC | POST webhook with tampered body | 401 returned, sync NOT enqueued |

## US3: Aggregation & Dashboard

| # | Test Case | Steps | Expected Result |
|---|-----------|-------|-----------------|
| 15 | Aggregated balance | View dashboard with 2 accounts (EUR + USD) | Totals grouped by currency |
| 16 | Money flow chart | Check monthly inflow/outflow bars | Credit transactions = inflow, debit = outflow |
| 17 | Top categories | View category breakdown | Sorted by spend descending, percentages correct |
| 18 | Transfer detection | Matching debit+credit across accounts | Transfer pair identified, not double-counted |
| 19 | Empty state | New user, no accounts | Dashboard shows "Connect your first account" prompt |

## Edge Cases

| Scenario | Expected Behaviour |
|----------|--------------------|
| Network timeout during sync | Polly retries 3× with exponential backoff (5m, 15m, 1h) |
| Bank returns 0 transactions | Sync completes successfully; count = 0 |
| > 500 transactions in one sync | All pages fetched, all deduplicated |
| Token expired mid-request | 401 returned, Angular redirects to login |
| Rate limit hit | 429 with Retry-After header |
| DB connection lost | 503 "Database unavailable. Try again in 1 minute." |

## Regression Checklist

- [ ] All existing unit tests pass (`dotnet test`)
- [ ] No new compiler warnings
- [ ] Swagger UI renders all endpoints at `/swagger`
- [ ] Health check responds at `/health/ready`
- [ ] Audit logs written for every data access (spot-check `audit_logs` table)

## Browser / Device Compatibility

| Browser | Version | Status |
|---------|---------|--------|
| Chrome  | Latest  | Primary target |
| Firefox | Latest  | Supported |
| Safari  | Latest  | Supported |
| Edge    | Latest  | Supported |
| Mobile Chrome | Android 12+ | Responsive layout verified |
| Mobile Safari | iOS 16+     | Responsive layout verified |
