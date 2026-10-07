# Multi-Currency Handling Guide

## Overview

Finance Sentry supports multiple currencies (EUR, USD, GBP, UAH). Each bank account has a single `currency` field (ISO 4217 code) and its transactions are stored in that currency; rows and per-account balances are shown in the account's own currency.

## Dashboard Display

Dashboard totals (net worth, income, spending, top categories, projection) and the net-worth history chart are shown in the profile's base currency (USD when unset), converted once at the response boundary, and the Transactions month summary uses the same currency. Amounts are formatted symbol-first (see [Money Display](claude/frontend-rules.md)). Which figures are converted, where, and with which rates is owned by [money-semantics.md §3](money-semantics.md); the FIRE card follows the same rule.

## Exchange Rates

Rates come from the process-wide table refreshed daily by the FX job; a currency without a rate falls back 1:1 (see [money-semantics.md §3](money-semantics.md)).

## GDPR Compliance

Transaction data is retained for 24 months per FR-008. Upon deletion of a bank account, all associated transactions are soft-deleted and excluded from dashboard calculations.
