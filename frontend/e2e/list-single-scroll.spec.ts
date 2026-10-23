import {expect, type Page, test} from '@playwright/test';

import {mockAccounts} from './support/accounts-mocks';
import {BROKERAGE_HOLDINGS} from './support/dossier-mocks';
import {PENDING_AAPL} from './support/recommendation-mocks';

const API = '**/api/v1';
const DESKTOP = {width: 1440, height: 900};
const PHONE = {width: 390, height: 844};
const LONG_COUNT = 40;
const PAGE_SIZE = 50;
const TIMESTAMP = '2026-10-05T00:00:00Z';
const SETTLE_MS = 600;

const AUTH_RESPONSE = {
  user: {
    id: 'test-user-id',
    email: 'test@gmail.com',
    roles: ['Owner'],
    permissions: ['ai.use', 'users.manage'],
  },
  expiresAt: '2027-01-01T00:00:00Z',
};

const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});

const holding = (i: number, category: 'banking' | 'brokerage', provider: string) => ({
  accountId: `${provider}-${i}`,
  bankName: provider,
  accountType: category === 'banking' ? 'Current' : 'Stock',
  accountNumberLast4: `${provider}${i}`.slice(-4),
  currency: 'USD',
  provider,
  category,
  currentBalance: 100,
  balanceInBaseCurrency: 100,
  syncStatus: 'synced',
  lastSyncTimestamp: TIMESTAMP,
});

// One bank with `count` accounts and one broker with `count` positions; no categories when empty.
function wealthSummary(count: number): unknown {
  const section = (category: 'banking' | 'brokerage', provider: string, name: string) => ({
    category,
    totalInBaseCurrency: count * 100,
    institutionCount: 1,
    institutions: [
      {
        institutionId: `${provider}-1`,
        provider,
        name,
        category,
        totalInBaseCurrency: count * 100,
        syncStatus: 'synced',
        lastSyncTimestamp: TIMESTAMP,
        lastSuccessfulSyncTimestamp: TIMESTAMP,
        accounts: Array.from({length: count}, (_, i) => holding(i, category, provider)),
        cards: null,
      },
    ],
  });
  return {
    totalNetWorth: count * 200,
    baseCurrency: 'USD',
    appliedFilters: {category: null, provider: null},
    categories:
      count === 0
        ? []
        : [
            section('banking', 'truelayer', 'Monzo'),
            section('brokerage', 'ibkr', 'Interactive Brokers'),
          ],
  };
}

async function mockApis(page: Page, count: number): Promise<void> {
  const rows = (make: (i: number) => unknown) => Array.from({length: count}, (_, i) => make(i));
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 0})));
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  await page.route(`${API}/alerts?**`, route =>
    route.fulfill(
      json({
        items: rows(i => ({
          id: `alert-${i}`,
          type: 'Opportunity',
          severity: 'Info',
          title: `Alert ${i}`,
          message: 'A message long enough to take a row.',
          referenceId: null,
          referenceLabel: null,
          isRead: true,
          isResolved: false,
          createdAt: TIMESTAMP,
          resolvedAt: null,
        })),
        totalCount: count,
        unreadCount: 0,
        page: 1,
        pageSize: PAGE_SIZE,
        totalPages: 1,
      })
    )
  );
  await page.route(`${API}/events/upcoming**`, route => {
    const today = new Date().toISOString().slice(0, 10);
    return route.fulfill(
      json({
        items: rows(i => ({
          kind: 'macro',
          date: today,
          time: null,
          subject: 'US',
          title: `Synthetic event ${i}`,
          detail: null,
          isEstimate: false,
          source: 'https://www.federalreserve.gov',
          referenceId: null,
        })),
        from: today,
        to: today,
        sources: [],
      })
    );
  });
  await page.route(`${API}/people`, route =>
    route.fulfill(
      json(
        rows(i => ({
          id: `u-${i}`,
          email: `person${i}@example.com`,
          roles: ['Member'],
          status: 'Active',
        }))
      )
    )
  );
  await page.route(`${API}/research/watchlist`, route =>
    route.fulfill(
      json(
        rows(i => ({
          id: `w-${i}`,
          ticker: `TCK${i}`,
          exchange: 'NASDAQ',
          note: 'Wait for the next print',
          addedAt: TIMESTAMP,
        }))
      )
    )
  );
  await page.route(`${API}/budgets/summary**`, route =>
    route.fulfill(
      json({
        year: 2026,
        month: 10,
        items: rows(i => ({
          id: `b-${i}`,
          category: `CATEGORY_${i}`,
          categoryLabel: `Category ${i}`,
          monthlyLimit: 100,
          spent: 40,
          remaining: 60,
          isOverBudget: false,
          currency: 'USD',
          paceRatio: 0.4,
          projectedMonthEndSpend: 80,
          isOffPace: false,
        })),
        totalLimit: count * 100,
        totalSpent: count * 40,
      })
    )
  );
  await page.route(`${API}/subscriptions?**`, route =>
    route.fulfill(
      json({
        items: rows(i => ({
          id: `s-${i}`,
          merchantName: `Merchant ${i}`,
          cadence: 'monthly',
          averageAmount: 10,
          lastKnownAmount: 10,
          monthlyEquivalent: 10,
          currency: 'USD',
          lastChargeDate: TIMESTAMP,
          nextExpectedDate: '2026-11-05T00:00:00Z',
          status: 'active',
          occurrenceCount: 12,
          kind: 'subscription',
          termCount: null,
          endDate: null,
          startDate: null,
          remainingPayments: null,
          isManual: false,
          isTracked: true,
        })),
        totalCount: count,
        hasInsufficientHistory: false,
      })
    )
  );
  const bucket = {
    monthly: count * 10,
    next12Months: count * 120,
    remainingCommitment: null,
    activeCount: count,
    hasUnknownSchedule: false,
  };
  await page.route(`${API}/subscriptions/summary`, route =>
    route.fulfill(
      json({
        subscriptions: bucket,
        installments: {...bucket, monthly: 0, next12Months: 0, activeCount: 0},
        combined: bucket,
        potentiallyCancelledCount: 0,
        currency: 'USD',
      })
    )
  );
  await page.route(`${API}/research/recommendations`, route =>
    route.fulfill(
      json({
        pending: rows(i => ({...PENDING_AAPL, id: `rec-${i}`, ticker: `TCK${i}`})),
        decided: [],
      })
    )
  );
  await page.route(`${API}/research/scorecards**`, route => route.fulfill(json([])));
  await page.route(`${API}/dashboard/flow-breakdown**`, route =>
    route.fulfill(
      json({
        month: '2026-10',
        items: rows(i => ({
          transactionId: `f-${i}`,
          accountId: 'acc',
          bankName: 'Bank',
          accountLast4: '4821',
          currency: 'USD',
          amount: -10,
          amountUsd: -10,
          date: TIMESTAMP,
          description: `Merchant ${i}`,
          merchantName: `Merchant ${i}`,
          category: 'GROCERIES',
          direction: 'out',
          bucket: 'spending',
          counterpartyName: null,
          flowRole: null,
        })),
      })
    )
  );
  await page.route(`${API}/accounts/transactions**`, route =>
    route.fulfill(
      json({
        items: rows(i => ({
          transactionId: `tx-${i}`,
          accountId: 'acc',
          bankName: 'Bank',
          currency: 'USD',
          amount: -10,
          amountUsd: -10,
          date: TIMESTAMP,
          postedDate: null,
          description: `Merchant ${i}`,
          transactionType: 'debit',
          merchantCategory: null,
          isPending: false,
          createdAt: TIMESTAMP,
          updatedAt: TIMESTAMP,
        })),
        totalCount: count,
        hasMore: false,
      })
    )
  );
  await mockAccounts(page);
  await page.route(`${API}/wealth/summary`, route => route.fulfill(json(wealthSummary(count))));
  await page.route(`${API}/brokerage/holdings`, route =>
    route.fulfill(
      json({
        ...BROKERAGE_HOLDINGS,
        positions: rows(i => ({
          symbol: `SYM${i}`,
          instrumentType: 'STK',
          quantity: 1,
          usdValue: 100,
          costBasisUsd: 90,
          averageCostUsd: 90,
        })),
        totalUsdValue: count * 100,
      })
    )
  );
}

// Every element that scrolls vertically right now, `main` and the document included.
function scrollers(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const found: string[] = [];
    for (const el of document.querySelectorAll('*')) {
      const overflowY = getComputedStyle(el).overflowY;
      if (/(auto|scroll)/.test(overflowY) && el.scrollHeight > el.clientHeight + 1) {
        found.push(el.tagName.toLowerCase());
      }
    }
    const doc = document.documentElement;
    if (doc.scrollHeight > doc.clientHeight + 1) {
      found.push('document');
    }
    return found;
  });
}

// A page that keeps a form or the whole book above its list can be taller than an empty list needs,
// so its one scroll region may legitimately scroll when the list is empty - it is never any other
// element.
const PAGES = [
  {path: '/alerts'},
  {path: '/events'},
  {path: '/transactions'},
  {path: '/subscriptions'},
  {path: '/budgets'},
  {path: '/ledger/decisions'},
  {path: '/accounts'},
  {path: '/investments?provider=ibkr'},
  {path: '/settings/people', tallWhenEmpty: true},
  {path: '/watchlist', tallWhenEmpty: true},
  {path: '/dashboard/breakdown?month=2026-10', tallWhenEmpty: true},
];
const VIEWPORTS = [
  {name: '1440', size: DESKTOP},
  {name: '390', size: PHONE},
];

for (const {name, size} of VIEWPORTS) {
  test.describe(`list pages scroll only the list (${name} wide)`, () => {
    test.use({viewport: size});

    for (const {path, tallWhenEmpty} of PAGES) {
      test(`${path}: a long list is the one scroll container`, async ({page}) => {
        await mockApis(page, LONG_COUNT);
        await page.goto(path);
        await expect(page.locator('cmn-page-scroll')).toBeVisible();
        await page.waitForTimeout(SETTLE_MS);
        expect(await scrollers(page)).toEqual(['cmn-page-scroll']);
      });

      test(`${path}: an empty list scrolls nothing`, async ({page}) => {
        await mockApis(page, 0);
        await page.goto(path);
        await expect(page.locator('cmn-empty-state')).toBeVisible();
        await page.waitForTimeout(SETTLE_MS);
        const found = await scrollers(page);
        expect(tallWhenEmpty ? found.filter(el => el !== 'cmn-page-scroll') : found).toEqual([]);
      });
    }
  });
}
