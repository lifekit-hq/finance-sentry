import {expect} from '@playwright/test';

import {describeVisualBaseline, mockGet} from './support/visual-baseline';

const ACCOUNT = {
  accountId: 'acc-monzo',
  bankName: 'Monzo',
  accountType: 'Current',
  accountNumberLast4: '4821',
  currency: 'USD',
};

function tx(
  id: string,
  description: string,
  date: string,
  amount: number,
  category: string | null,
  isPending = false
): Record<string, unknown> {
  return {
    transactionId: id,
    accountId: ACCOUNT.accountId,
    bankName: ACCOUNT.bankName,
    currency: 'USD',
    amount: Math.abs(amount),
    amountUsd: Math.abs(amount),
    date,
    postedDate: isPending ? null : date,
    description,
    transactionType: amount < 0 ? 'debit' : 'credit',
    merchantCategory: category,
    isPending,
    createdAt: date,
    updatedAt: date,
  };
}

const LEDGER = {
  items: [
    tx('t-1', 'Corner Cafe', '2026-09-15T08:12:00Z', -4.5, 'FOOD_AND_DRINK', true),
    tx('t-2', 'Tesco Groceries', '2026-09-14T17:40:00Z', -62.18, 'GROCERIES'),
    tx('t-3', 'Salary — Acme Ltd', '2026-09-14T06:00:00Z', 3200, 'INCOME'),
    tx('t-4', 'Netflix', '2026-09-12T03:00:00Z', -15.99, 'ENTERTAINMENT'),
    tx('t-5', 'Uber', '2026-09-11T21:15:00Z', -18.4, 'TRANSPORT'),
    tx('t-6', 'Rent', '2026-09-01T09:00:00Z', -1450, 'RENT'),
  ],
  totalCount: 6,
  hasMore: false,
};

describeVisualBaseline({
  name: 'transactions',
  title: 'Transaction ledger',
  path: '/transactions',
  mocks: async page => {
    await mockGet(page, 'accounts', {accounts: [ACCOUNT]});
    await mockGet(page, 'accounts/transactions**', LEDGER);
    await mockGet(page, 'dashboard/aggregated**', {
      aggregatedBalance: {USD: 50000},
      totalNetWorthUsd: 50000,
      baseCurrency: 'USD',
      accountCount: 1,
      accountsByType: {banking: 1},
      monthlyFlow: [],
      topCategories: [],
      lastSyncTimestamp: '2026-09-15T10:30:00Z',
    });
  },
  ready: async page => {
    await expect(page.getByTestId('ledger-row')).toHaveCount(LEDGER.items.length);
  },
});
