import {expect, type Page, test} from '@playwright/test';

import {BROKERAGE_GROWTH, mockAccounts} from './support/accounts-mocks';
import {API, AUTH_RESPONSE, mockApis as mockDossierApis} from './support/dossier-mocks';
import {json, mockGet} from './support/visual-baseline';

// Home's Investments card: it sits under the net-worth chart and its range chips, the hero no longer
// carries an "Invested change" line, the card holds a fixed-height skeleton while positions load,
// and each top holding opens its dossier.
const POSITIONS_LATENCY_MS = 600;
const SKELETON_HEIGHT_PX = 640;
const PHONE = {width: 390, height: 1500};

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

const HISTORY = [
  ['2026-09-01', 43_900, 5_300, 700],
  ['2026-09-15', 43_400, 5_600, 1_000],
].map(([snapshotDate, cash, brokerageInvested, cryptoInvested]) => ({
  snapshotDate,
  bankingTotal: cash,
  brokerageTotal: brokerageInvested,
  cryptoTotal: cryptoInvested,
  totalNetWorth: Number(cash) + Number(brokerageInvested) + Number(cryptoInvested),
  currency: 'USD',
  cashTotal: cash,
  brokerageInvested,
  cryptoInvested,
}));

async function openHome(page: Page, positionsLatencyMs = 0): Promise<void> {
  await mockDossierApis(page);
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/dashboard/aggregated**`, route => route.fulfill(json(DASHBOARD)));
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill(json({snapshots: HISTORY, hasHistory: true}))
  );
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  await page.route(`${API}/accounts`, route => route.fulfill(json({accounts: []})));
  await mockAccounts(page);
  if (positionsLatencyMs > 0) {
    await page.route(`${API}/brokerage/holdings`, async route => {
      await new Promise(resolve => setTimeout(resolve, positionsLatencyMs));
      await route.fallback();
    });
  }
  await mockGet(page, 'wealth/brokerage/growth', BROKERAGE_GROWTH);
  await page.goto('/dashboard');
}

test.describe('Home Investments card', () => {
  test('sits after the range chips and carries the invested total, allocation and growth', async ({
    page,
  }) => {
    await openHome(page);

    const card = page.getByTestId('investments-card');
    await expect(page.getByTestId('investments-card-total')).toContainText('$');
    await expect(card.locator('h2').first()).toContainText('Invested');
    await expect(page.getByTestId('investments-card-allocation')).toContainText('Equities');
    await expect(page.getByTestId('brokerage-growth-growth')).toBeVisible();

    const chips = page.locator('lk-chip-row[label="History range"]');
    const chipsBottom = (await chips.boundingBox())?.y ?? 0;
    const cardTop = (await card.boundingBox())?.y ?? 0;
    expect(cardTop).toBeGreaterThan(chipsBottom);
  });

  test('drops the hero Invested change line', async ({page}) => {
    await openHome(page);

    await expect(page.getByTestId('net-worth-value')).toBeVisible();
    await expect(page.getByText('Invested change')).toHaveCount(0);
    await expect(page.getByTestId('net-worth-invested-change-slot')).toHaveCount(0);
  });

  test('holds a fixed-height skeleton while positions load', async ({page}) => {
    await openHome(page, POSITIONS_LATENCY_MS);

    const skeleton = page.getByTestId('investments-card-skeleton');
    await expect(skeleton).toBeVisible();
    expect((await skeleton.boundingBox())?.height).toBe(SKELETON_HEIGHT_PX);
    await expect(page.getByTestId('investments-card-total')).toBeVisible();
  });

  test('lists the top holdings, each opening its dossier', async ({page}) => {
    await openHome(page);

    const rows = page.getByTestId('investments-card-holding');
    await expect(rows.first()).toBeVisible();
    await expect(page.getByTestId('investments-card-top-holdings')).toContainText('MSFT');

    await page
      .getByTestId('investments-card-holding-link')
      .and(page.locator('[href="/assets/AAPL"]'))
      .click();
    await expect(page).toHaveURL(/\/assets\/AAPL$/);
  });

  test('fits a phone without horizontal scroll', async ({page}) => {
    await page.setViewportSize(PHONE);
    await openHome(page);

    await expect(page.getByTestId('investments-card-total')).toBeVisible();
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(overflow).toBeLessThanOrEqual(0);
  });
});
