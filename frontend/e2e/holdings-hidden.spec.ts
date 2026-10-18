import {expect, type Page, test} from '@playwright/test';

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

async function open(page: Page): Promise<void> {
  await mockApis(page);
  await page.route(`${API}/crypto/holdings`, route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify(CRYPTO)})
  );
  await page.route(`${API}/brokerage/holdings`, route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify(BROKERAGE_HOLDINGS)})
  );
  await page.goto('/accounts/investments');
  await expect(page.getByTestId('holdings-hidden')).toBeVisible();
}

async function shot(page: Page, name: string): Promise<void> {
  if (SHOTS_DIR) {
    await page.getByTestId('holdings-hidden').scrollIntoViewIfNeeded();
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

    test('closes a group with a collapsed row, opens in place and keeps totals whole', async ({
      page,
    }) => {
      await open(page);
      const hidden = page.getByTestId('holdings-hidden');
      const details = hidden.locator('details');
      await expect(details).toHaveJSProperty('open', false);
      await expect(hidden).toContainText('2 hidden · $0.62');
      await expect(page.getByTestId('holdings-total-value')).toContainText('$68,500.62');
      await shot(page, `holdings-hidden-collapsed-${label}`);

      const summary = hidden.locator('summary');
      const box = await summary.boundingBox();
      expect(box?.height).toBeGreaterThanOrEqual(MIN_TARGET_PX);
      const offset = async (): Promise<number> => {
        const top = (await hidden.boundingBox())?.y ?? Number.NaN;
        const summaryTop = (await summary.boundingBox())?.y ?? Number.NaN;
        return summaryTop - top;
      };
      const before = await offset();
      await summary.click();
      await expect(details).toHaveJSProperty('open', true);
      await expect(hidden.getByTestId('holdings-hidden-rows')).toContainText('SHIB');
      await expect(hidden.getByTestId('holdings-hidden-rows')).toContainText('DOGE');
      expect(await offset()).toBe(before);
      await shot(page, `holdings-hidden-expanded-${label}`);
    });
  });
}
