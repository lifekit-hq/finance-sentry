import {expect, type Page, test} from '@playwright/test';

// Origin-agnostic glob, NOT the dev apiBaseUrl — the production build file-replaces
// environment.ts so apiBaseUrl becomes the relative '/api/v1'. See dashboard-drilldown.spec.ts.
const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner']},
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

// Banking dwarfs the market sleeves on purpose: a return assumption that compounded cash
// would add 5,000 here rather than the 500 the assertions expect.
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

const PROJECTION_HEADING = 'Where this is heading';
const PROJECTED_LABEL = 'Projected net worth in 12 months';

test.describe('Dashboard — twelve-month net worth projection', () => {
  test('projects from the median complete month at the 0% default', async ({page}) => {
    await mockApis(page, [...COMPLETE_MONTHS, CURRENT_MONTH]);
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    await expect(page.getByText(PROJECTION_HEADING)).toBeVisible();

    // 10,000 today + median 1,000/mo x 12. A mean baseline (1,933) would read $33,200.
    const tile = page.getByText(PROJECTED_LABEL).locator('..');
    await expect(tile).toContainText('$22,000');
    await expect(
      page.getByText('Median saved per month, based on 3 complete months')
    ).toBeVisible();
    await expect(
      page.getByText('Assumes no market return — this is contributions only.')
    ).toBeVisible();
  });

  test('hides the tile entirely below three complete months', async ({page}) => {
    await mockApis(page, [...COMPLETE_MONTHS.slice(1), CURRENT_MONTH]);
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    await expect(page.getByText(PROJECTION_HEADING)).toHaveCount(0);
    await expect(page.getByText(PROJECTED_LABEL)).toHaveCount(0);
  });

  test('compounds only the market sleeves when a return is selected, without refetching', async ({
    page,
  }) => {
    const dataRequests: string[] = [];
    page.on('request', req => {
      const url = req.url();
      if (url.includes('dashboard/aggregated') || url.includes('net-worth/history')) {
        dataRequests.push(url);
      }
    });

    await mockApis(page, [...COMPLETE_MONTHS, CURRENT_MONTH]);
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    const tile = page.getByText(PROJECTED_LABEL).locator('..');
    await expect(tile).toContainText('$22,000');
    const requestsAfterLoad = dataRequests.length;

    await page.getByRole('button', {name: '5%', exact: true}).click();

    // +5% of the 10,000 in brokerage + crypto. Banking cash (100,000) does not compound.
    await expect(tile).toContainText('$22,500');
    await expect(
      page.getByText('Assumes 5%/yr on the $10K already in brokerage and crypto.')
    ).toBeVisible();

    expect(dataRequests.length).toBe(requestsAfterLoad);
  });

  test('breaks the headline into addends and moves only the market-return one', async ({page}) => {
    await mockApis(page, [...COMPLETE_MONTHS, CURRENT_MONTH]);
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    const today = page.getByText('Today', {exact: true}).locator('..');
    const contributions = page.getByText('Contributions', {exact: true}).locator('..');
    const marketReturn = page.getByText('Market return', {exact: true}).locator('..');

    // 10,000 + 12,000 + 0 = the $22,000 headline. The flat default reads as a plain $0.
    await expect(today).toContainText('$10,000');
    await expect(contributions).toContainText('+$12,000');
    await expect(marketReturn).toContainText('$0');

    await page.getByRole('button', {name: '5%', exact: true}).click();

    // Only the assumption line moves; today and contributions are behaviour, not a guess.
    await expect(marketReturn).toContainText('+$500');
    await expect(today).toContainText('$10,000');
    await expect(contributions).toContainText('+$12,000');
    await expect(page.getByText(PROJECTED_LABEL).locator('..')).toContainText('$22,500');
  });
});
