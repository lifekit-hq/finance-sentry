import {expect, type Page, test} from '@playwright/test';

import {mockAccounts} from './support/accounts-mocks';
import {API, AUTH_RESPONSE} from './support/dossier-mocks';

// A range change whose request fails must settle: static placeholders (no sheen), never the old
// range's figures under the new label, and a Retry that brings the new range in.
const PHONE = {width: 390, height: 900};
const SHOT_DIR = process.env['FAILED_RANGE_SHOT_DIR'];

const json = (body: unknown) => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

const snapshot = (snapshotDate: string, banking: number) => ({
  snapshotDate,
  bankingTotal: banking,
  brokerageTotal: 5000,
  cryptoTotal: 1000,
  totalNetWorth: banking + 6000,
  currency: 'USD',
});

const dashboard = (netWorth: number) => ({
  aggregatedBalance: {USD: netWorth},
  totalNetWorthUsd: netWorth,
  baseCurrency: 'USD',
  accountCount: 3,
  accountsByType: {banking: 2, brokerage: 1},
  monthlyFlow: [],
  topCategories: [],
  lastSyncTimestamp: null,
});

const OLD_FROM_MS = 200 * 24 * 60 * 60 * 1000;
const FAIL = {status: 500, contentType: 'application/json', body: '{}'};

/** Serves the 3M range, then fails whichever 1Y request `failing` names until `heal` is called. */
async function mockApis(page: Page, failing: 'dashboard' | 'history') {
  let healed = false;
  // 3M reaches back about 90 days, 1Y about 365: anything older than 200 days is the 1Y request.
  const isOneYear = (url: string): boolean => {
    const from = new URL(url).searchParams.get('from');
    return from !== null && Date.parse(from) < Date.now() - OLD_FROM_MS;
  };
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  // The Home Investments card reads the holdings feeds and growth split on its own.
  await mockAccounts(page);
  await page.route(`${API}/accounts`, route => route.fulfill(json({accounts: []})));
  await page.route(`${API}/dashboard/aggregated**`, route => {
    const oneYear = route.request().url().includes('months=12');
    return oneYear && failing === 'dashboard' && !healed
      ? route.fulfill(FAIL)
      : route.fulfill(json(dashboard(oneYear ? 222_222 : 111_111)));
  });
  await page.route(`${API}/net-worth/history**`, route => {
    const oneYear = isOneYear(route.request().url());
    return oneYear && failing === 'history' && !healed
      ? route.fulfill(FAIL)
      : route.fulfill(
          json({
            snapshots: [snapshot('2026-07-01', 40_000), snapshot('2026-08-01', 43_000)],
            hasHistory: true,
          })
        );
  });
  // The net-worth bridge card follows the same range; it is not the request under test.
  await page.route(`${API}/net-worth/bridge**`, route =>
    route.fulfill(
      json({quality: 'UNAVAILABLE', reasons: [{code: 'SHORT_HISTORY'}], currency: 'USD'})
    )
  );
  return {heal: (): void => void (healed = true)};
}

async function pick1Y(page: Page): Promise<void> {
  await page
    .getByRole('group', {name: 'History range'})
    .getByRole('button', {name: '1Y', exact: true})
    .click();
}

test.describe('Dashboard failed range change, 390px phone', () => {
  test.use({viewport: PHONE});

  for (const failing of ['history', 'dashboard'] as const) {
    test(`${failing} request fails: static placeholders, no old figures, Retry recovers`, async ({
      page,
    }) => {
      const api = await mockApis(page, failing);
      await page.goto('/dashboard');
      await expect(page.getByTestId('net-worth-value')).toContainText('$111,111');

      await pick1Y(page);

      await expect(page.getByText(/Failed to load/).first()).toBeVisible();
      await expect(page.locator('.cmn-skeleton-sweep.after\\:hidden').first()).toBeVisible();
      await expect(page.locator('.cmn-skeleton-sweep:not(.after\\:hidden)')).toHaveCount(0);
      await expect(page.getByText('$111,111')).toHaveCount(0);
      if (SHOT_DIR) {
        await page.screenshot({path: `${SHOT_DIR}/${failing}-failure-390.png`, fullPage: true});
      }

      api.heal();
      await page.getByRole('button', {name: 'Retry'}).first().click();

      await expect(page.locator('.cmn-skeleton-sweep')).toHaveCount(0);
      await expect(page.getByText(/Failed to load/)).toHaveCount(0);
      if (failing === 'dashboard') {
        await expect(page.getByTestId('net-worth-value')).toContainText('$222,222');
      }
    });
  }
});
