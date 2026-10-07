import {expect, type Page, test} from '@playwright/test';

import {API, mockApis} from './support/dossier-mocks';

const PHONES = {
  'iPhone 390x844': {width: 390, height: 844},
  'small phone 320x568': {width: 320, height: 568},
} as const;
const DESKTOP = {width: 1440, height: 900};
const SIGNED_IN_EMAIL = 'test@gmail.com';

async function openPage(page: Page, path: string): Promise<void> {
  await mockApis(page);
  await page.route(`${API}/alerts/unread-count`, route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify({count: 0})})
  );
  await page.goto(path);
}

async function expectNoHorizontalScroll(page: Page): Promise<void> {
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(overflow).toBeLessThanOrEqual(0);
}

for (const [name, viewport] of Object.entries(PHONES)) {
  test.describe(`phone contract (${name})`, () => {
    test.use({viewport, hasTouch: true, isMobile: true});

    test('the dossier is one page: back chevron, the symbol as its only h1, no in-page back', async ({
      page,
    }) => {
      await openPage(page, '/assets/AAPL');

      await expect(page.getByRole('heading', {level: 1})).toHaveCount(1);
      await expect(page.getByRole('heading', {name: 'AAPL', level: 1})).toBeVisible();
      await expect(page.getByTestId('dossier-back')).toHaveCount(0);

      await page.getByRole('button', {name: 'Back'}).click();
      await expect(page).toHaveURL(/\/accounts\/investments/);
      await expectNoHorizontalScroll(page);
    });

    test('a tab root has a large title and no back chevron', async ({page}) => {
      await openPage(page, '/budgets');

      await expect(page.getByRole('heading', {name: 'Budgets', level: 1})).toBeVisible();
      await expect(page.getByRole('button', {name: 'Back'})).toHaveCount(0);
      await expect(page.getByRole('button', {name: 'Add budget'})).toBeVisible();
    });

    test('More lists the destinations and the account items; the avatar is gone', async ({
      page,
    }) => {
      await openPage(page, '/more');

      const links = page.locator('fns-more-page nav a');
      await expect(links).toHaveText([/Budgets/, /Subscriptions/, /Events/, /Ledger/, /Settings/]);
      const logout = page.getByTestId('more-logout');
      await expect(logout).toContainText('Log out');
      await expect(logout).toContainText(SIGNED_IN_EMAIL);
      await expect(page.getByRole('button', {name: 'Account menu'})).toBeHidden();
      await expectNoHorizontalScroll(page);
    });

    test('Ledger is reached through More and the chat button stays off the phone', async ({
      page,
    }) => {
      await openPage(page, '/dashboard');
      await expect(page.getByRole('button', {name: 'Open Ledger chat'})).toHaveCount(0);

      await page.goto('/more');
      await page.locator('fns-more-page nav a', {hasText: 'Ledger'}).click();
      await expect(page).toHaveURL(/\/ledger/);
      await expect(page.getByRole('button', {name: 'Open Ledger chat'})).toHaveCount(0);
    });
  });
}

test.describe('desktop keeps the avatar and the chat button', () => {
  test.use({viewport: DESKTOP});

  test('top bar avatar menu and the floating chat button render', async ({page}) => {
    await openPage(page, '/dashboard');

    await expect(page.getByRole('button', {name: 'Account menu'})).toBeVisible();
    await expect(page.getByRole('button', {name: 'Open Ledger chat'})).toBeVisible();
  });
});
