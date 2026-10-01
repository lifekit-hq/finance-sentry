import {expect, type Page, test} from '@playwright/test';

// Origin-agnostic glob, NOT the dev apiBaseUrl — the production build file-replaces
// environment.ts so apiBaseUrl becomes the relative '/api/v1'. See dashboard-drilldown.spec.ts.
const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {
    id: 'test-user-id',
    email: 'test@gmail.com',
    roles: ['Owner'],
    permissions: [
      'connections.manage',
      'ai.use',
      'mcp.connect',
      'mcp.service',
      'ops.admin',
      'users.manage',
    ],
  },
  expiresAt: '2027-01-01T00:00:00Z',
};

const MONTH_KEY_PAD = 2;

/** Month key ("yyyy-MM", the backend's format) `monthsAgo` months before the current one. */
function monthKey(monthsAgo: number): string {
  const now = new Date();
  const target = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - monthsAgo, 1));
  const month = String(target.getUTCMonth() + 1).padStart(MONTH_KEY_PAD, '0');
  return `${target.getUTCFullYear()}-${month}`;
}

function flow(monthsAgo: number, inflow: number, outflow: number): Record<string, unknown> {
  return {
    month: monthKey(monthsAgo),
    currency: 'USD',
    inflow,
    outflow,
    net: inflow - outflow,
    inflowUsd: inflow,
    outflowUsd: outflow,
    netUsd: inflow - outflow,
  };
}

/**
 * Three complete months netting −200 / +1000 / +5000, so the median is 1000 and the mean is
 * 1933 — the assertions below would move if the tile ever switched to a mean. The current
 * month is deliberately noisy; it must not reach the baseline.
 */
const COMPLETE_MONTHS = [flow(3, 1000, 1200), flow(2, 5000, 4000), flow(1, 6000, 1000)];
const CURRENT_MONTH = flow(0, 100, 9000);

function dashboardPayload(monthlyFlow: Record<string, unknown>[]): Record<string, unknown> {
  return {
    aggregatedBalance: {USD: 10_000},
    totalNetWorthUsd: 10_000,
    accountCount: 2,
    accountsByType: {banking: 1, brokerage: 1},
    monthlyFlow,
    topCategories: [],
    lastSyncTimestamp: null,
  };
}

const NET_WORTH_HISTORY = {
  snapshots: [
    {
      snapshotDate: '2026-06-01',
      totalNetWorth: 108_000,
      bankingTotal: 100_000,
      brokerageTotal: 6000,
      cryptoTotal: 2000,
      staleSleeves: null,
    },
    {
      snapshotDate: '2026-07-01',
      totalNetWorth: 110_000,
      bankingTotal: 100_000,
      brokerageTotal: 8000,
      cryptoTotal: 2000,
      staleSleeves: null,
    },
  ],
  hasHistory: true,
};

async function mockApis(page: Page, monthlyFlow: Record<string, unknown>[]): Promise<void> {
  await page.route(`${API}/auth/me`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(AUTH_RESPONSE),
    })
  );
  await page.route(`${API}/auth/refresh`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(AUTH_RESPONSE),
    })
  );
  await page.route(`${API}/dashboard/aggregated**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(dashboardPayload(monthlyFlow)),
    })
  );
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(NET_WORTH_HISTORY),
    })
  );
}

const PROJECTION_LINE = /At this pace:/;

test.describe('Dashboard — twelve-month net worth projection', () => {
  test('projects one line from the median complete month', async ({page}) => {
    await mockApis(page, [...COMPLETE_MONTHS, CURRENT_MONTH]);
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    // 10,000 today + median 1,000/mo x 12. A mean baseline (1,933) would read $33,200.
    const line = page.getByText(PROJECTION_LINE);
    await expect(line).toContainText('$22,000');
    await expect(line).toContainText('in 12 months');
    await expect(line).toHaveAttribute(
      'title',
      'Median saved per month, based on 3 complete months'
    );
  });

  test('hides the line below three complete months', async ({page}) => {
    await mockApis(page, [...COMPLETE_MONTHS.slice(1), CURRENT_MONTH]);
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    await expect(page.getByText(PROJECTION_LINE)).toHaveCount(0);
  });

  test('offers no return-rate assumption toggles', async ({page}) => {
    await mockApis(page, [...COMPLETE_MONTHS, CURRENT_MONTH]);
    await page.goto('/dashboard');
    await expect(page.getByText(PROJECTION_LINE)).toBeVisible();

    await expect(page.getByRole('button', {name: '5%', exact: true})).toHaveCount(0);
  });
});
