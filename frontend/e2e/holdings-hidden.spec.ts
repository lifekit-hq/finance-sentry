import {expect, type Locator, type Page, test} from '@playwright/test';

import {API, BROKERAGE_HOLDINGS, CRYPTO_HOLDINGS, mockApis} from './support/dossier-mocks';

const PHONE = {width: 390, height: 844};
const DESKTOP = {width: 1280, height: 900};
const SHOTS_DIR = process.env['HOLDINGS_HIDDEN_SHOTS'];
const MIN_TARGET_PX = 44;
const SCROLL_SETTLE_MS = 400;

const CRYPTO = {
  ...CRYPTO_HOLDINGS,
  totalUsdValue: 30_000.62,
  holdings: [
    {
      asset: 'BTC',
      freeQuantity: 0.5,
      lockedQuantity: 0,
      usdValue: 30_000,
      provider: 'binance',
      isFiat: false,
    },
    {
      asset: 'SHIB',
      freeQuantity: 20_000,
      lockedQuantity: 0,
      usdValue: 0.42,
      provider: 'binance',
      isFiat: false,
      hiddenReason: 'small',
    },
    {
      asset: 'DOGE',
      freeQuantity: 2,
      lockedQuantity: 0,
      usdValue: 0.2,
      provider: 'binance',
      isFiat: false,
      hiddenReason: 'small',
    },
  ],
};

const SUMMARY = {
  totalNetWorth: 68_500.62,
  baseCurrency: 'USD',
  appliedFilters: {category: null, provider: null},
  categories: [
    {
      category: 'crypto',
      totalInBaseCurrency: 30_000.62,
      institutionCount: 1,
      institutions: [
        {
          institutionId: 'binance-1',
          provider: 'binance',
          name: 'Binance',
          category: 'crypto',
          totalInBaseCurrency: 30_000.62,
          syncStatus: 'synced',
          lastSyncTimestamp: '2026-09-01T10:00:00Z',
          lastSuccessfulSyncTimestamp: '2026-09-01T10:00:00Z',
          hiddenCount: 2,
          hiddenTotal: 0.62,
          accounts: [],
          cards: null,
        },
      ],
    },
  ],
};

async function open(page: Page): Promise<Locator> {
  await mockApis(page);
  await page.route(`${API}/wealth/summary`, route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify(SUMMARY)})
  );
  await page.route(`${API}/wealth/manual-holdings`, route =>
    route.fulfill({contentType: 'application/json', body: '[]'})
  );
  await page.route(`${API}/crypto/holdings`, route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify(CRYPTO)})
  );
  await page.route(`${API}/brokerage/holdings`, route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify(BROKERAGE_HOLDINGS)})
  );
  await page.goto('/accounts/list?provider=binance');
  const hidden = page.getByTestId('hidden-rows');
  await expect(hidden).toBeVisible();
  return hidden;
}

async function shot(page: Page, name: string): Promise<void> {
  if (SHOTS_DIR) {
    await page.getByTestId('hidden-rows').scrollIntoViewIfNeeded();
    await page.waitForTimeout(SCROLL_SETTLE_MS);
    await page.mouse.move(0, 0);
    await page.screenshot({path: `${SHOTS_DIR}/${name}.png`});
  }
}

for (const [label, viewport] of [
  ['390', PHONE],
  ['1280', DESKTOP],
] as const) {
  test.describe(`hidden positions at ${label}`, () => {
    test.use({viewport, hasTouch: label === '390', isMobile: label === '390'});

    test('closes a group with a collapsed row, opens in place and keeps the row stable', async ({
      page,
    }) => {
      const hidden = await open(page);
      const rowToggle = hidden.locator('button[aria-controls]');
      await expect(page.getByTestId('position-ticker').first()).toHaveText('BTC');
      await expect(rowToggle).toHaveAttribute('aria-expanded', 'false');
      await expect(hidden).toContainText('2 hidden · $0.62');
      await shot(page, `holdings-hidden-collapsed-${label}`);

      // The toggle's ::after covers the whole row, so the row is the press target.
      const summary = rowToggle.locator('..');
      const box = await summary.boundingBox();
      expect(box?.height).toBeGreaterThanOrEqual(MIN_TARGET_PX);
      const offset = async (): Promise<number> => {
        const top = (await hidden.boundingBox())?.y ?? Number.NaN;
        const summaryTop = (await summary.boundingBox())?.y ?? Number.NaN;
        return summaryTop - top;
      };
      const before = await offset();
      await rowToggle.click();
      await expect(rowToggle).toHaveAttribute('aria-expanded', 'true');
      await expect(hidden.getByTestId('hidden-rows-list')).toContainText('SHIB');
      await expect(hidden.getByTestId('hidden-rows-list')).toContainText('DOGE');
      expect(await offset()).toBe(before);
      await shot(page, `holdings-hidden-expanded-${label}`);
    });
  });
}
