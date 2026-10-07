import {expect, type Page, test} from '@playwright/test';

// #851: a EUR-base profile must see euro totals on the dashboard and the ledger header,
// never the hard-coded dollar sign the pages used to print above EUR rows.
const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: []},
  expiresAt: '2027-01-01T00:00:00Z',
};

function currentUtcMonthKey(): string {
  const now = new Date();
  return `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(2, '0')}`;
}

const EUR_DASHBOARD = {
  aggregatedBalance: {EUR: 2000},
  totalNetWorthUsd: 2000,
  baseCurrency: 'EUR',
  accountCount: 1,
  accountsByType: {banking: 1},
  monthlyFlow: [
    {
      month: currentUtcMonthKey(),
      currency: 'EUR',
      inflow: 4000,
      outflow: 3100,
      net: 900,
      inflowUsd: 4000,
      outflowUsd: 3100,
      netUsd: 900,
      familySupportOutflowUsd: 0,
    },
  ],
  topCategories: [{category: 'FOOD_AND_DRINK', totalSpend: 800, percentOfTotal: 26}],
  lastSyncTimestamp: null,
};

const EUR_FIRE = {
  status: 'Projected',
  target: 930000,
  currentNetWorth: 2000,
  monthlySavings: 900,
  annualSpend: 37200,
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
  projectedDate: '2051-03-01',
  monthsToFire: 300,
  hasStaleSleeves: false,
  baseCurrency: 'EUR',
};

const EUR_TRANSACTIONS = {
  items: [
    {
      transactionId: 'tx-1',
      accountId: 'acc-1',
      bankName: 'AIB',
      currency: 'EUR',
      amount: 42,
      amountUsd: 45,
      date: '2026-10-01T10:00:00Z',
      postedDate: '2026-10-01T10:00:00Z',
      description: 'Synthetic groceries',
      transactionType: 'debit',
      merchantCategory: 'FOOD_AND_DRINK',
      isPending: false,
      createdAt: '2026-10-01T00:00:00Z',
      updatedAt: '2026-10-01T00:00:00Z',
    },
  ],
  totalCount: 1,
  hasMore: false,
};

const json = (body: unknown) => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

async function mockApis(page: Page): Promise<void> {
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/dashboard/aggregated**`, route => route.fulfill(json(EUR_DASHBOARD)));
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill(json({snapshots: [], hasHistory: false}))
  );
  await page.route(`${API}/wealth/fire`, route => route.fulfill(json(EUR_FIRE)));
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  await page.route(`${API}/accounts`, route =>
    route.fulfill(
      json({accounts: [{accountId: 'acc-1', bankName: 'AIB', accountNumberLast4: '1111'}]})
    )
  );
  await page.route(`${API}/accounts/transactions**`, route =>
    route.fulfill(json(EUR_TRANSACTIONS))
  );
}

test.describe('Base-currency totals (#851)', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('dashboard totals are in euros', async ({page}) => {
    await page.goto('/dashboard');

    const spending = page.getByRole('button', {name: /view spending details/i});
    await expect(spending).toContainText('€3,100.00');
    await expect(spending).not.toContainText('$');
    await expect(page.getByRole('button', {name: /view income details/i})).toContainText(
      '€4,000.00'
    );
    await expect(page.getByTestId('net-worth-value')).toContainText('€2,000.00');
    await expect(page.getByTestId('net-worth-value')).not.toContainText('$');
  });

  test('FIRE card states its arithmetic in euros', async ({page}) => {
    await page.goto('/dashboard');

    const assumptions = page.getByTestId('fire-assumptions');
    await expect(assumptions).toContainText('Target €930,000.00');
    await expect(assumptions).toContainText('€37,200.00');
    await expect(assumptions).toContainText('€2,000.00 net worth');
    await expect(assumptions).toContainText('€900.00 a month');
    await expect(assumptions).not.toContainText('$');
  });

  test('ledger header reads in euros above the EUR rows', async ({page}) => {
    await page.goto('/transactions');

    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    const summary = page.getByTestId('ledger-summary');
    await expect(summary).toContainText('€3,100.00 out this month');
    await expect(summary).not.toContainText('$');
  });
});
