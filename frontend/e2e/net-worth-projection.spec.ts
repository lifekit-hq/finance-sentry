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

const json = (body: unknown) => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

const DASHBOARD = {
  aggregatedBalance: {USD: 10_000},
  totalNetWorthUsd: 10_000,
  accountCount: 2,
  accountsByType: {banking: 1, brokerage: 1},
  monthlyFlow: [],
  topCategories: [],
  lastSyncTimestamp: null,
};

/**
 * The backend's twelve-month projection (M10): $10,000 today plus a $1,000 median month,
 * compounded at the shared 2% / 5% / 8% real returns.
 */
const PROJECTED = {
  status: 'Projected',
  horizonMonths: 12,
  currentNetWorth: 10_000,
  monthlySavings: 1_000,
  completeMonths: 3,
  projectedNetWorth: {low: 22_312.4, central: 22_788.9, high: 23_270.2},
  realAnnualReturnRange: {low: 0.02, central: 0.05, high: 0.08},
  hasStaleSleeves: false,
  baseCurrency: 'USD',
};

const INSUFFICIENT = {
  ...PROJECTED,
  status: 'InsufficientHistory',
  completeMonths: 2,
  projectedNetWorth: null,
};

async function mockApis(page: Page, projection: Record<string, unknown>): Promise<void> {
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/dashboard/aggregated**`, route => route.fulfill(json(DASHBOARD)));
  await page.route(`${API}/dashboard/flow-breakdown**`, route =>
    route.fulfill(json({month: '2026-09', items: []}))
  );
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill(json({snapshots: [], hasHistory: false}))
  );
  await page.route(`${API}/wealth/projection`, route => route.fulfill(json(projection)));
}

test.describe('Breakdown — twelve-month net worth projection', () => {
  test('shows the central figure, its range and its assumptions in words', async ({page}) => {
    await mockApis(page, PROJECTED);
    await page.goto('/dashboard/breakdown');

    const line = page.getByTestId('net-worth-projection');
    await expect(line).toContainText('In 12 months: about $22,789');
    await expect(line).toContainText('($22,312 to $23,270)');

    const assumptions = page.getByTestId('net-worth-projection-assumptions');
    await expect(assumptions).toContainText('Your $10,000 net worth plus $1,000 a month saved');
    await expect(assumptions).toContainText('your median over 3 complete months');
    await expect(assumptions).toContainText('5% real (after-inflation) annual return');
    await expect(assumptions).toContainText('the range uses 2% to 8%');
    await expect(assumptions).toContainText('Hypothetical, not a guarantee.');
  });

  test('hides the line below three complete months', async ({page}) => {
    await mockApis(page, INSUFFICIENT);
    const answered = page.waitForResponse('**/wealth/projection');
    await page.goto('/dashboard/breakdown');
    await answered;
    await expect(page.getByTestId('breakdown-outlook')).toBeVisible();

    await expect(page.getByTestId('net-worth-projection')).toHaveCount(0);
    await expect(page.getByTestId('net-worth-projection-assumptions')).toHaveCount(0);
  });

  test('fits a 390px phone without horizontal scroll', async ({page}) => {
    await page.setViewportSize({width: 390, height: 900});
    await mockApis(page, PROJECTED);
    await page.goto('/dashboard/breakdown');
    await expect(page.getByTestId('net-worth-projection')).toBeVisible();

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(overflow).toBeLessThanOrEqual(0);
  });
});
