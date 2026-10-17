import {expect, type Page, test} from '@playwright/test';

import {mockAccounts} from './support/accounts-mocks';
import {API, mockApis} from './support/dossier-mocks';
import {mockGet} from './support/visual-baseline';

const PHONE = {width: 390, height: 844};
const DESKTOP = {width: 1280, height: 900};
const SHOTS_DIR = process.env['ACCOUNTS_CLOSED_SHOTS'];
const MIN_TARGET_PX = 44;
const SCROLL_SETTLE_MS = 400;

function bankAccount(overrides: Record<string, unknown>): Record<string, unknown> {
  return {
    accountId: 'x',
    bankName: 'Monobank',
    accountType: 'checking',
    accountNumberLast4: '0000',
    currency: 'UAH',
    provider: 'monobank',
    ownerName: 'Test User',
    currentBalance: 0,
    availableBalance: 0,
    syncStatus: 'synced',
    lastSyncTimestamp: '2026-09-15T10:00:00Z',
    lastSyncDurationMs: null,
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

const ACCOUNTS = {
  accounts: [
    bankAccount({accountId: 'acc-live', accountNumberLast4: '1111', productType: 'black'}),
    bankAccount({
      accountId: 'acc-gone',
      accountNumberLast4: '4242',
      productType: 'white',
      currentBalance: 1250.5,
      closedAt: '2026-08-30T08:00:00Z',
      closedReason: 'absent_from_list',
    }),
    bankAccount({
      accountId: 'acc-reissued',
      accountNumberLast4: '9876',
      productType: 'black',
      currentBalance: 0,
      closedAt: '2026-07-02T08:00:00Z',
      closedReason: 'reissued',
    }),
  ],
};

async function open(page: Page): Promise<void> {
  await mockApis(page);
  await mockAccounts(page);
  await mockGet(page, 'accounts', ACCOUNTS);
  await page.route(`${API}/alerts/unread-count`, route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify({count: 0})})
  );
  await page.goto('/accounts/list');
  await expect(page.getByTestId('closed-accounts')).toBeVisible();
}

async function shot(page: Page, name: string): Promise<void> {
  if (SHOTS_DIR) {
    // The shell scrolls inside <main>, so show the bottom of the page where the section sits.
    await page.locator('main').evaluate(el => (el.scrollTop = el.scrollHeight));
    await page.waitForTimeout(SCROLL_SETTLE_MS);
    await page.locator('main').evaluate(el => (el.scrollTop = el.scrollHeight));
    await page.mouse.move(0, 0);
    await page.screenshot({path: `${SHOTS_DIR}/${name}.png`});
  }
}

for (const [label, viewport] of [
  ['390', PHONE],
  ['1280', DESKTOP],
] as const) {
  test.describe(`closed accounts at ${label}`, () => {
    test.use({viewport, hasTouch: label === '390', isMobile: label === '390'});

    test('stays collapsed, opens in place and keeps the page where it was', async ({page}) => {
      await open(page);
      const section = page.getByTestId('closed-accounts');
      const details = section.locator('details');
      await expect(details).toHaveJSProperty('open', false);
      await expect(section).toContainText('Closed accounts (2)');
      await shot(page, `closed-collapsed-${label}`);

      const header = section.locator('summary');
      await header.scrollIntoViewIfNeeded();
      const before = await header.boundingBox();
      await header.click();
      await expect(details).toHaveJSProperty('open', true);
      const rows = section.getByTestId('closed-account-row');
      await expect(rows).toHaveCount(2);
      await expect(rows.first()).toContainText('No longer listed by Monobank');
      await expect(rows.first()).toContainText('•••• 4242');
      await expect(rows.first().getByTestId('closed-last-balance')).toContainText('₴1,250.50');
      await expect(rows.first()).toContainText('Last known');
      // The section grows downwards: its header stays under the thumb.
      expect((await header.boundingBox())?.y).toBe(before?.y);
      await shot(page, `closed-expanded-${label}`);

      const target = await rows.first().locator('button').first().boundingBox();
      expect(target?.height).toBeGreaterThanOrEqual(MIN_TARGET_PX);
    });

    test('removes a closed account only after the confirm', async ({page}) => {
      await open(page);
      let removed = 0;
      await page.route(`${API}/accounts/acc-gone`, route => {
        removed += 1;
        return route.fulfill({status: 204});
      });
      const section = page.getByTestId('closed-accounts');
      await section.locator('summary').click();
      await section
        .getByRole('button', {name: /^Actions for /})
        .first()
        .click();
      await page.getByRole('menuitem', {name: 'Remove'}).click();
      await expect(page.locator('section[role="dialog"]')).toContainText("can't be undone");
      expect(removed).toBe(0);
      await page.getByRole('button', {name: 'Cancel'}).click();
      expect(removed).toBe(0);
    });
  });
}
