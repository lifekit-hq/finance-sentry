import type {Page} from '@playwright/test';

import {mockAccounts} from './accounts-mocks';
import {API, AUTH_RESPONSE} from './dossier-mocks';
import {json} from './visual-baseline';

// The dashboard's fixture data, shared by its pixel baseline and the screen gates.
const DASHBOARD = {
  aggregatedBalance: {USD: 50000},
  totalNetWorthUsd: 50000,
  cashUsd: 12000,
  investedUsd: 38000,
  baseCurrency: 'USD',
  accountCount: 3,
  accountsByType: {banking: 2, brokerage: 1},
  monthlyFlow: [
    {
      month: '2026-08',
      currency: 'USD',
      inflow: 5000,
      outflow: 3000,
      net: 2000,
      inflowUsd: 5000,
      outflowUsd: 3000,
      netUsd: 2000,
      familySupportOutflowUsd: 0,
    },
    {
      month: '2026-09',
      currency: 'USD',
      inflow: 4800,
      outflow: 2900,
      net: 1900,
      inflowUsd: 4800,
      outflowUsd: 2900,
      netUsd: 1900,
      familySupportOutflowUsd: 800,
      investedOutflowUsd: 600,
    },
  ],
  topCategories: [
    {category: 'FOOD_AND_DRINK', totalSpend: 800, percentOfTotal: 30},
    {category: 'TRAVEL', totalSpend: 500, percentOfTotal: 18},
  ],
  lastSyncTimestamp: '2026-09-15T10:30:00Z',
};

// Enough snapshots for the hero chart, so the baseline shows the figure, chart and range control as one card.
// The first two days predate the cash / invested split (drawn as the "No split" band); the last two
// carry it, with cash as the remainder, so the baseline shows the stacked Cash / invested chart, its legend
// and the "Where it is" rows read off the latest day.
const HISTORY = [
  ['2026-07-15', 41_000, 5_000, 1_000, null],
  ['2026-08-15', 42_500, 5_500, 1_200, null],
  ['2026-09-01', 43_000, 5_800, 1_100, [43_900, 5_300, 700]],
  ['2026-09-15', 43_000, 5_800, 1_200, [43_400, 5_600, 1_000]],
].map(([snapshotDate, bankingTotal, brokerageTotal, cryptoTotal, split]) => ({
  snapshotDate,
  bankingTotal,
  brokerageTotal,
  cryptoTotal,
  totalNetWorth: Number(bankingTotal) + Number(brokerageTotal) + Number(cryptoTotal),
  currency: 'USD',
  ...(split && {cashTotal: split[0], brokerageInvested: split[1], cryptoInvested: split[2]}),
}));

// The bridge over the default range (15 Jul 47,000 → 15 Sep 50,000), the residual within tolerance.
const BRIDGE = {
  from: '2026-07-15',
  to: '2026-09-15',
  startValue: 47_000,
  endValue: 50_000,
  savings: 1_900,
  investmentReturn: 900,
  market: 870,
  income: 40,
  fees: 10,
  taxes: 0,
  fxTranslation: 150,
  revaluation: 0,
  newlyLinked: 0,
  unlinked: 0,
  residual: 50,
  residualAboveTolerance: false,
  quality: 'COMPLETE',
  reasons: [],
  currency: 'USD',
};

// The newest unread alert, Home's one insight, with two more waiting in Alerts.
const UNREAD_ALERTS = {
  items: [
    {
      id: 'alert-1',
      type: 'LowBalance',
      severity: 'Warning',
      title: 'Low balance on Monzo',
      message: 'Your Monzo current account dropped below $500.',
      referenceId: null,
      referenceLabel: null,
      isRead: false,
      isResolved: false,
      createdAt: '2026-09-15T08:00:00Z',
      resolvedAt: null,
      occurrenceCount: 1,
      lastOccurredAt: '2026-09-15T08:00:00Z',
    },
  ],
  totalCount: 3,
  unreadCount: 3,
  page: 1,
  pageSize: 1,
  totalPages: 3,
};

/** Registers every request the dashboard makes; the caller pins the clock and opens the route. */
export async function mockDashboard(page: Page): Promise<void> {
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/dashboard/aggregated**`, route => route.fulfill(json(DASHBOARD)));
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill(json({snapshots: HISTORY, hasHistory: true}))
  );
  await page.route(`${API}/net-worth/bridge**`, route => route.fulfill(json(BRIDGE)));
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  await page.route(`${API}/accounts`, route => route.fulfill(json({accounts: []})));
  await page.route(`${API}/alerts?**`, route => route.fulfill(json(UNREAD_ALERTS)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 3})));
  await mockAccounts(page);
}
