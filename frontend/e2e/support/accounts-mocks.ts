import type {Page} from '@playwright/test';

import {BROKERAGE_HOLDINGS} from './dossier-mocks';
import {mockGet} from './visual-baseline';

const SYNCED_AT = '2026-09-15T10:00:00Z';

function account(
  accountId: string,
  bankName: string,
  accountType: string,
  last4: string,
  provider: string,
  category: 'banking' | 'brokerage' | 'crypto',
  balance: number
): Record<string, unknown> {
  return {
    accountId,
    bankName,
    accountType,
    accountNumberLast4: last4,
    currency: 'USD',
    provider,
    category,
    currentBalance: balance,
    balanceInBaseCurrency: balance,
    syncStatus: 'synced',
    lastSyncTimestamp: SYNCED_AT,
  };
}

function institution(
  institutionId: string,
  provider: string,
  name: string,
  category: 'banking' | 'brokerage' | 'crypto',
  accounts: Record<string, unknown>[]
): Record<string, unknown> {
  return {
    institutionId,
    provider,
    name,
    category,
    totalInBaseCurrency: accounts.reduce((sum, a) => sum + Number(a['balanceInBaseCurrency']), 0),
    syncStatus: 'synced',
    lastSyncTimestamp: SYNCED_AT,
    lastSuccessfulSyncTimestamp: SYNCED_AT,
    accounts,
    cards: null,
  };
}

function section(
  category: 'banking' | 'brokerage' | 'crypto',
  institutions: Record<string, unknown>[]
): Record<string, unknown> {
  return {
    category,
    totalInBaseCurrency: institutions.reduce((sum, i) => sum + Number(i['totalInBaseCurrency']), 0),
    institutionCount: institutions.length,
    institutions,
  };
}

const MONZO_CURRENT = 4200;
const MONZO_SAVINGS = 7800;
const AAPL_VALUE = 17500;
const MSFT_VALUE = 21000;
const BTC_VALUE = 3200;

export const WEALTH_SUMMARY_FULL = {
  totalNetWorth: 53700,
  baseCurrency: 'USD',
  appliedFilters: {category: null, provider: null},
  categories: [
    section('banking', [
      institution('monzo-1', 'truelayer', 'Monzo', 'banking', [
        account('acc-monzo', 'Monzo', 'Current', '4821', 'truelayer', 'banking', MONZO_CURRENT),
        account('acc-monzo-sv', 'Monzo', 'Savings', '9034', 'truelayer', 'banking', MONZO_SAVINGS),
      ]),
    ]),
    section('brokerage', [
      institution('ibkr-1', 'ibkr', 'Interactive Brokers', 'brokerage', [
        account(
          'acc-aapl',
          'Interactive Brokers',
          'Stock',
          'AAPL',
          'ibkr',
          'brokerage',
          AAPL_VALUE
        ),
        account(
          'acc-msft',
          'Interactive Brokers',
          'Stock',
          'MSFT',
          'ibkr',
          'brokerage',
          MSFT_VALUE
        ),
      ]),
    ]),
    section('crypto', [
      institution('binance-1', 'binance', 'Binance', 'crypto', [
        account('acc-btc', 'Binance', 'Spot', 'BTC', 'binance', 'crypto', BTC_VALUE),
      ]),
    ]),
  ],
};

export const CRYPTO_HOLDINGS_FULL = {
  provider: 'binance',
  syncedAt: SYNCED_AT,
  isStale: false,
  holdings: [
    {
      asset: 'BTC',
      freeQuantity: 0.04,
      lockedQuantity: 0,
      usdValue: 2600,
      provider: 'binance',
      isFiat: false,
    },
    {
      asset: 'ETH',
      freeQuantity: 0.2,
      lockedQuantity: 0,
      usdValue: 600,
      provider: 'binance',
      isFiat: false,
    },
  ],
  totalUsdValue: 3200,
};

/** The accounts page and the investments tab read the wealth summary and both holdings feeds. */
export async function mockAccounts(page: Page): Promise<void> {
  await mockGet(page, 'wealth/summary', WEALTH_SUMMARY_FULL);
  await mockGet(page, 'brokerage/holdings', BROKERAGE_HOLDINGS);
  await mockGet(page, 'crypto/holdings', CRYPTO_HOLDINGS_FULL);
}
