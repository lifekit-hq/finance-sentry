import {expect, type Page, test} from '@playwright/test';

import {mockAccounts} from './support/accounts-mocks';
import {API, AUTH_RESPONSE} from './support/dossier-mocks';
import {mockHome} from './support/home-mocks';

// A headline figure that is not COMPLETE carries a badge; one tap opens the reason sheet.
// The badge and its reason need a 44 x 44 px hit area (mobile standard section 3).
const PHONE = {width: 390, height: 900};
const MIN_HIT_PX = 44;

const json = (body: unknown) => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

const headline = (overrides: Record<string, unknown>) => ({
  amount: 50_000,
  percent: null,
  method: 'VALUE',
  basis: 'MARKET_VALUE',
  scope: 'umbrella',
  window: {from: null, to: '2026-10-15'},
  quality: 'COMPLETE',
  basisStatus: 'COMPLETE',
  coverage: 1,
  reasons: [],
  asOf: '2026-10-15T08:00:00Z',
  currency: 'USD',
  ...overrides,
});

const DASHBOARD = {
  aggregatedBalance: {USD: 50_000},
  totalNetWorthUsd: 50_000,
  baseCurrency: 'USD',
  accountCount: 3,
  accountsByType: {banking: 2, brokerage: 1},
  monthlyFlow: [],
  topCategories: [],
  lastSyncTimestamp: null,
};

async function mockApis(page: Page, netWorthHeadline: unknown): Promise<void> {
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/dashboard/aggregated**`, route =>
    route.fulfill(json({...DASHBOARD, netWorthHeadline}))
  );
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  // The Home Investments card reads the holdings feeds and growth split on its own.
  await mockAccounts(page);
  await page.route(`${API}/accounts`, route => route.fulfill(json({accounts: []})));
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill(json({snapshots: [], hasHistory: false}))
  );
}

test.describe('Headline quality badge, 390px phone', () => {
  test.beforeEach(async ({page}) => {
    await page.setViewportSize(PHONE);
  });

  test('a COMPLETE figure shows no badge', async ({page}) => {
    await mockApis(page, headline({}));
    await page.goto('/dashboard');
    await expect(page.getByTestId('net-worth-value')).toBeVisible();
    await expect(page.getByTestId('headline-badge')).toHaveCount(0);
  });

  test('a stale source shows "Stale" and one tap opens its reason', async ({page}) => {
    await mockApis(
      page,
      headline({
        quality: 'DEGRADED',
        reasons: [{code: 'STALE_SOURCE', source: 'monobank'}],
      })
    );
    await page.goto('/dashboard');
    const badge = page.getByTestId('headline-badge').first();
    await expect(badge).toContainText('Stale');
    const box = await badge.boundingBox();
    expect(box?.width).toBeGreaterThanOrEqual(MIN_HIT_PX);
    expect(box?.height).toBeGreaterThanOrEqual(MIN_HIT_PX);

    await badge.click();
    const sheet = page.getByTestId('headline-reason-sheet');
    await expect(sheet).toBeVisible();
    await expect(page.getByTestId('headline-reasons')).toContainText(/monobank/i);
  });

  test('an unknown reason code falls back to a generic line', async ({page}) => {
    await mockApis(page, headline({quality: 'ESTIMATED', reasons: [{code: 'SOMETHING_NEW'}]}));
    await page.goto('/dashboard');
    const badge = page.getByTestId('headline-badge').first();
    await expect(badge).toContainText('Estimated');
    await badge.click();
    await expect(page.getByTestId('headline-reasons').locator('li')).toHaveCount(1);
  });

  test('an unavailable figure reads N/A, never zero', async ({page}) => {
    await mockApis(
      page,
      headline({amount: null, quality: 'UNAVAILABLE', reasons: [{code: 'NOT_MEASURABLE'}]})
    );
    await page.goto('/dashboard');
    await expect(page.getByTestId('net-worth-value')).toContainText('N/A');
    await expect(page.getByTestId('net-worth-value')).not.toContainText('$0');
  });

  test('a failed brokerage feed with no crypto keeps the Home investments card, not the empty book', async ({
    page,
  }) => {
    await mockHome(page);
    await page.route(`${API}/brokerage/holdings`, route =>
      route.fulfill({status: 500, contentType: 'application/json', body: '{}'})
    );
    await page.route(`${API}/crypto/holdings`, route =>
      route.fulfill(json({provider: 'binance', holdings: [], totalUsdValue: 0}))
    );
    await page.goto('/dashboard');
    await expect(page.getByTestId('investments-card-total')).toBeVisible();
    await expect(page.getByTestId('investments-card-quality')).toBeVisible();
    await expect(page.locator('body')).not.toContainText('No positions yet');
  });

  test('both holdings feeds failing renders N/A with a badge, never $0', async ({page}) => {
    await mockHome(page);
    await page.route(`${API}/brokerage/holdings`, route =>
      route.fulfill({status: 500, contentType: 'application/json', body: '{}'})
    );
    await page.route(`${API}/crypto/holdings`, route =>
      route.fulfill({status: 500, contentType: 'application/json', body: '{}'})
    );
    await page.goto('/dashboard');
    await expect(page.getByTestId('investments-card-total')).toContainText('N/A');
    await expect(page.getByTestId('investments-card-total')).not.toContainText('$0');
  });
});
