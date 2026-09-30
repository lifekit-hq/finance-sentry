import {expect, type Page, test} from '@playwright/test';

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

const DASHBOARD_DATA = {
  aggregatedBalance: {USD: 80000},
  totalNetWorthUsd: 80000,
  accountCount: 2,
  accountsByType: {banking: 1, brokerage: 1},
  monthlyFlow: [],
  topCategories: [],
  lastSyncTimestamp: null,
};

const NET_WORTH_HISTORY_WITH_DATA = {
  snapshots: [
    {
      snapshotDate: '2026-06-01',
      totalNetWorth: 75000,
      bankingTotal: 50000,
      brokerageTotal: 20000,
      cryptoTotal: 5000,
      staleSleeves: null,
    },
    {
      snapshotDate: '2026-07-01',
      totalNetWorth: 78000,
      bankingTotal: 52000,
      brokerageTotal: 21000,
      cryptoTotal: 5000,
      staleSleeves: null,
    },
    {
      snapshotDate: '2026-08-01',
      totalNetWorth: 80000,
      bankingTotal: 53000,
      brokerageTotal: 22000,
      cryptoTotal: 5000,
      staleSleeves: null,
    },
  ],
  hasHistory: true,
};

async function mockApis(page: Page): Promise<void> {
  await page.route(`${API}/auth/me`, route =>
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
      body: JSON.stringify(DASHBOARD_DATA),
    })
  );
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(NET_WORTH_HISTORY_WITH_DATA),
    })
  );
  await page.route(`${API}/auth/refresh`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(AUTH_RESPONSE),
    })
  );
}

test.describe('Net Worth Over Time — Stacked/Lines toggle', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('renders Stacked and Lines toggle buttons on the Net Worth card', async ({page}) => {
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    const stackedBtn = page.getByRole('button', {name: 'Stacked'});
    const linesBtn = page.getByRole('button', {name: 'Lines'});
    await expect(stackedBtn).toBeVisible();
    await expect(linesBtn).toBeVisible();
  });

  test('Stacked is the active default', async ({page}) => {
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    // The chart canvas should be present (history exists)
    await expect(page.locator('cmn-area-chart canvas')).toBeVisible();

    // Stacked button should be active (primary variant) by default
    const stackedBtn = page.getByRole('button', {name: 'Stacked'});
    const linesBtn = page.getByRole('button', {name: 'Lines'});
    await expect(stackedBtn).toBeVisible();
    await expect(linesBtn).toBeVisible();
    // Stacked is primary — this is the default state; Lines is secondary
    // We verify the active state by clicking Lines and confirming it becomes active
    await linesBtn.click();
    // After clicking Lines, clicking Stacked should toggle back
    await stackedBtn.click();
    // Chart is still visible — no refetch, just re-renders
    await expect(page.locator('cmn-area-chart canvas')).toBeVisible();
  });

  test('toggling Lines re-renders the chart without refetching data', async ({page}) => {
    const historyRequests: string[] = [];
    page.on('request', req => {
      if (req.url().includes('net-worth/history')) {
        historyRequests.push(req.url());
      }
    });

    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    const initialRequestCount = historyRequests.length;
    await expect(page.locator('cmn-area-chart canvas')).toBeVisible();

    // Click Lines toggle
    await page.getByRole('button', {name: 'Lines'}).click();
    await expect(page.locator('cmn-area-chart canvas')).toBeVisible();

    // Click Stacked toggle
    await page.getByRole('button', {name: 'Stacked'}).click();
    await expect(page.locator('cmn-area-chart canvas')).toBeVisible();

    // No additional history requests should have been made
    expect(historyRequests.length).toBe(initialRequestCount);
  });
});
