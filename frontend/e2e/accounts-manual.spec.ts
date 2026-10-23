import {expect, type Page, test} from '@playwright/test';

import {mockAccounts, WEALTH_SUMMARY_FULL} from './support/accounts-mocks';
import {API, mockApis} from './support/dossier-mocks';
import {FROZEN_NOW, mockGet} from './support/visual-baseline';

const PHONE = {width: 390, height: 844};
const DESKTOP = {width: 1280, height: 900};
const SHOTS_DIR = process.env['ACCOUNTS_MANUAL_SHOTS'];
const MIN_TARGET_PX = 44;
const SCROLL_SETTLE_MS = 400;
const STEAM_VALUE = 40;

const STEAM = {
  id: 'manual-1',
  label: 'Steam wallet',
  amount: STEAM_VALUE,
  currency: 'USD',
  asOf: '2026-09-15',
  convertibility: 'InEconomy',
  reliability: 'L3Estimated',
  updatedAt: '2026-09-15T10:00:00Z',
};

// The summary the backend returns once the entry exists: one "other" institution beside the linked ones.
const SUMMARY_WITH_STEAM = {
  ...WEALTH_SUMMARY_FULL,
  totalNetWorth: WEALTH_SUMMARY_FULL.totalNetWorth + STEAM_VALUE,
  categories: [
    ...WEALTH_SUMMARY_FULL.categories,
    {
      category: 'other',
      totalInBaseCurrency: STEAM_VALUE,
      institutionCount: 1,
      institutions: [
        {
          institutionId: 'user-1',
          provider: 'manual',
          name: 'Entered by hand',
          category: 'other',
          totalInBaseCurrency: STEAM_VALUE,
          syncStatus: 'synced',
          lastSyncTimestamp: null,
          lastSuccessfulSyncTimestamp: null,
          accounts: [],
          cards: null,
        },
      ],
    },
  ],
};

async function open(page: Page, holdings: unknown[]): Promise<void> {
  await page.clock.install({time: FROZEN_NOW});
  await mockApis(page);
  await mockAccounts(page);
  await mockGet(page, 'accounts', {accounts: []});
  if (holdings.length > 0) {
    await mockGet(page, 'wealth/summary', SUMMARY_WITH_STEAM);
  }
  await page.route(`${API}/wealth/manual-holdings`, route =>
    route.request().method() === 'GET'
      ? route.fulfill({contentType: 'application/json', body: JSON.stringify(holdings)})
      : route.fallback()
  );
  await page.goto('/accounts/list');
  await expect(page.getByTestId('manual-holdings')).toBeVisible();
}

async function shot(page: Page, name: string, scrollToEnd = true): Promise<void> {
  if (SHOTS_DIR) {
    if (scrollToEnd) {
      // The list scrolls inside cmn-page-scroll, so show the bottom of the page where the section sits.
      await page.locator('cmn-page-scroll').evaluate(el => (el.scrollTop = el.scrollHeight));
      await page.waitForTimeout(SCROLL_SETTLE_MS);
    }
    await page.mouse.move(0, 0);
    await page.screenshot({path: `${SHOTS_DIR}/${name}.png`});
  }
}

for (const [label, viewport] of [
  ['390', PHONE],
  ['1280', DESKTOP],
] as const) {
  test.describe(`values entered by hand at ${label}`, () => {
    test.use({viewport, hasTouch: label === '390', isMobile: label === '390'});

    test('lists the entry with where it spends, its value and the section subtotal', async ({
      page,
    }) => {
      await open(page, [STEAM]);
      const section = page.getByTestId('manual-holdings');
      const row = section.getByTestId('manual-holding-row');
      await expect(row).toHaveCount(1);
      await expect(row).toContainText('Steam wallet');
      await expect(row).toContainText('Spendable only inside its platform');
      await expect(row.getByTestId('manual-holding-value')).toHaveText('$40.00');
      await expect(section.getByTestId('manual-subtotal')).toHaveText('$40.00');
      await expect(page.getByTestId('accounts-net-worth')).toContainText('$53,740.00');
      await shot(page, `manual-section-${label}`);

      const target = await section.getByTestId('add-manual-holding').boundingBox();
      expect(target?.height).toBeGreaterThanOrEqual(MIN_TARGET_PX);
    });

    test('adds a value at what it would fetch after fees, spendable only in its platform', async ({
      page,
    }) => {
      await open(page, []);
      const posted: unknown[] = [];
      await page.route(`${API}/wealth/manual-holdings`, route => {
        if (route.request().method() !== 'POST') {
          return route.fallback();
        }
        posted.push(route.request().postDataJSON());
        return route.fulfill({
          status: 201,
          contentType: 'application/json',
          body: JSON.stringify(STEAM),
        });
      });

      await page.getByTestId('add-manual-holding').click();
      const dialog = page.locator('section[role="dialog"]');
      await expect(dialog).toContainText('Add a value by hand');
      await expect(dialog).toContainText('What you would receive after fees');
      await dialog.getByLabel('Name').fill('Steam wallet');
      await dialog.getByLabel('Value').fill('40');
      await shot(page, `manual-dialog-${label}`, false);

      await dialog.getByRole('button', {name: 'Add'}).click();
      await expect(dialog).toBeHidden();
      expect(posted).toEqual([
        {
          label: 'Steam wallet',
          amount: STEAM_VALUE,
          currency: 'USD',
          asOf: '2026-09-15',
          convertibility: 'InEconomy',
        },
      ]);
    });

    test('removes an entry only after the confirm', async ({page}) => {
      await open(page, [STEAM]);
      let removed = 0;
      await page.route(`${API}/wealth/manual-holdings/manual-1`, route => {
        removed += 1;
        return route.fulfill({status: 204});
      });

      await page.getByRole('button', {name: 'Actions for Steam wallet'}).click();
      await page.getByRole('menuitem', {name: 'Remove'}).click();
      await expect(page.locator('section[role="dialog"]')).toContainText(
        'It leaves your total net worth.'
      );
      await shot(page, `manual-remove-${label}`, false);
      await page.getByRole('button', {name: 'Cancel'}).click();
      expect(removed).toBe(0);
    });
  });
}
