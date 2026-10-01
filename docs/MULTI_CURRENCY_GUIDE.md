# Multi-Currency Handling Guide

## Overview

Finance Sentry supports multiple currencies (EUR, USD, GBP, UAH) without automatic conversion.

## How It Works

- Each bank account has a single `currency` field (ISO 4217 code)
- Transaction amounts are stored in the account's base currency
- The aggregated dashboard shows totals **separately per currency** — EUR and USD are never summed together

## Dashboard Display

The aggregated balance section shows one card per currency:
```
€5,000.00
$1,000.00
```

Amounts are formatted symbol-first (see [Money Display](claude/frontend-rules.md)); conversion and aggregation rules live in [money-semantics.md](money-semantics.md).

## Monthly Flow Statistics

Monthly inflow/outflow statistics are grouped by currency-month. If you have EUR and USD accounts, you will see separate statistics for each.

## Top Categories

Category statistics aggregate spending across all currencies. Amounts are shown in their original currency — no conversion is performed.

## Exchange Rates

Currency conversion is **not supported** in the current version. Exchange rates and currency conversion are deferred to a future feature release.

## GDPR Compliance

Transaction data is retained for 24 months per FR-008. Upon deletion of a bank account, all associated transactions are soft-deleted and excluded from dashboard calculations.
