import {expect} from '@playwright/test';

import {mockGet, type VisualScreen} from '../visual-baseline';

function subscription(
  id: string,
  merchantName: string,
  amount: number,
  nextExpectedDate: string,
  extra: Record<string, unknown> = {}
): Record<string, unknown> {
  return {
    id,
    merchantName,
    cadence: 'monthly',
    averageAmount: amount,
    lastKnownAmount: amount,
    monthlyEquivalent: amount,
    currency: 'USD',
    lastChargeDate: '2026-08-20T00:00:00Z',
    nextExpectedDate,
    status: 'active',
    occurrenceCount: 12,
    kind: 'subscription',
    termCount: null,
    endDate: null,
    startDate: null,
    remainingPayments: null,
    isManual: false,
    isTracked: true,
    ...extra,
  };
}

const LIST = {
  items: [
    subscription('s-1', 'Netflix', 15.99, '2026-09-17T00:00:00Z'),
    subscription('s-2', 'Spotify', 11.99, '2026-09-22T00:00:00Z'),
    subscription('s-3', 'iCloud+', 2.99, '2026-10-02T00:00:00Z'),
    subscription('s-4', 'Gym membership', 39, '2026-10-05T00:00:00Z', {
      cadence: 'annual',
      monthlyEquivalent: 3.25,
      averageAmount: 39,
    }),
    subscription('s-5', 'Phone plan', 42, '2026-10-12T00:00:00Z', {
      kind: 'installment',
      termCount: 24,
      remainingPayments: 9,
      endDate: '2027-06-12T00:00:00Z',
    }),
  ],
  totalCount: 5,
  hasInsufficientHistory: false,
};

const BUCKET = {
  monthly: 0,
  next12Months: 0,
  remainingCommitment: null,
  activeCount: 0,
  hasUnknownSchedule: false,
};

const SUMMARY = {
  subscriptions: {...BUCKET, monthly: 34.22, next12Months: 410.64, activeCount: 4},
  installments: {
    ...BUCKET,
    monthly: 42,
    next12Months: 378,
    remainingCommitment: 378,
    activeCount: 1,
  },
  combined: {...BUCKET, monthly: 76.22, next12Months: 788.64, activeCount: 5},
  potentiallyCancelledCount: 0,
  currency: 'USD',
};

// The list mock's glob (`subscriptions?**`) also matches this path, so it needs its own: no foreign-currency
// plan, which keeps the insight off the baseline.
const FX_IMPACT = {
  baseCurrency: 'USD',
  plans: [],
  totals: {paidSaving: 0, remainingSaving: 0, totalSaving: 0, hasUnknownRemainder: false},
};

export const SUBSCRIPTIONS_SCREEN: VisualScreen = {
  name: 'subscriptions',
  title: 'Subscriptions',
  path: '/subscriptions',
  mocks: async page => {
    await mockGet(page, 'subscriptions?**', LIST);
    await mockGet(page, 'subscriptions/summary', SUMMARY);
    await mockGet(page, 'subscriptions/fx-impact', FX_IMPACT);
  },
  ready: async page => {
    await expect(page.getByText('Netflix').first()).toBeVisible();
  },
};
