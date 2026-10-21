import {expect, type Page, test} from '@playwright/test';

import {API, AUTH_RESPONSE} from './support/dossier-mocks';

const json = (body: unknown) => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

const DASHBOARD = {
  aggregatedBalance: {USD: 300_000},
  totalNetWorthUsd: 300_000,
  accountCount: 2,
  accountsByType: {banking: 1, brokerage: 1},
  monthlyFlow: [],
  topCategories: [],
  lastSyncTimestamp: null,
};

/** The backend's monthly-income lens (P5): $300,000 drawn at the shared 4% withdrawal rate. */
const SHOWN = {
  status: 'Shown',
  netWorth: 300_000,
  safeWithdrawalRate: 0.04,
  monthlyIncome: 1_000,
  hasStaleSleeves: false,
  baseCurrency: 'USD',
};

const NO_NET_WORTH = {...SHOWN, status: 'NoNetWorth', netWorth: -4_000, monthlyIncome: null};

async function mockApis(page: Page, lens: Record<string, unknown>): Promise<void> {
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/dashboard/aggregated**`, route => route.fulfill(json(DASHBOARD)));
  await page.route(`${API}/dashboard/flow-breakdown**`, route =>
    route.fulfill(json({month: '2026-09', items: []}))
  );
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill(json({snapshots: [], hasHistory: false}))
  );
  await page.route(`${API}/wealth/income-lens`, route => route.fulfill(json(lens)));
}

test.describe('Breakdown — monthly-income lens', () => {
  test('shows the net worth as monthly income with its assumption in words', async ({page}) => {
    await mockApis(page, SHOWN);
    await page.goto('/dashboard/breakdown');

    await expect(page.getByTestId('monthly-income-lens')).toContainText(
      'As income: about $1,000 a month'
    );
    const assumptions = page.getByTestId('monthly-income-lens-assumptions');
    await expect(assumptions).toContainText('Your $300,000 net worth drawn at 4% a year');
    await expect(assumptions).toContainText('the safe withdrawal rate your');
    await expect(assumptions).toContainText("In today's money. Hypothetical, not a guarantee.");
    await expect(page.getByTestId('monthly-income-lens-stale')).toHaveCount(0);
  });

  test('notes stale data when a source is stale', async ({page}) => {
    await mockApis(page, {...SHOWN, hasStaleSleeves: true});
    await page.goto('/dashboard/breakdown');

    await expect(page.getByTestId('monthly-income-lens-stale')).toContainText(
      'this is an estimate'
    );
  });

  test('renders nothing with no positive net worth', async ({page}) => {
    await mockApis(page, NO_NET_WORTH);
    const answered = page.waitForResponse('**/wealth/income-lens');
    await page.goto('/dashboard/breakdown');
    await answered;
    await expect(page.getByTestId('breakdown-outlook')).toBeVisible();

    await expect(page.getByTestId('monthly-income-lens')).toHaveCount(0);
    await expect(page.getByTestId('monthly-income-lens-assumptions')).toHaveCount(0);
  });

  test('fits a 390px phone without horizontal scroll', async ({page}) => {
    await page.setViewportSize({width: 390, height: 900});
    await mockApis(page, SHOWN);
    await page.goto('/dashboard/breakdown');
    await expect(page.getByTestId('monthly-income-lens')).toBeVisible();

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(overflow).toBeLessThanOrEqual(0);
  });
});
