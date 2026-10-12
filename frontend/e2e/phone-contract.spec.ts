import {expect, type Page, test} from '@playwright/test';

import {API, mockApis} from './support/dossier-mocks';

const PHONES = {
  'iPhone 390x844': {width: 390, height: 844},
  'small phone 320x568': {width: 320, height: 568},
} as const;
const DESKTOP = {width: 1440, height: 900};
const SIGNED_IN_EMAIL = 'test@gmail.com';
const SEEN_KEY = 'fns.whatsNew.lastSeen';
const WHATS_NEW = {
  versions: [
    {
      version: '1.14.0',
      date: '2026-10-05',
      notes: [
        {text: 'You can show only money in or only money out.', owner: false},
        {text: 'You can invite people from Settings.', owner: true},
      ],
      changes: ['Group transactions by day'],
    },
    {version: '1.13.0', date: '2026-09-28', notes: [], changes: ['Older change']},
  ],
};

async function openWithStaleSeen(page: Page, path: string): Promise<void> {
  await page.addInitScript(key => localStorage.setItem(key, '1.13.0'), SEEN_KEY);
  await page.route('**/whats-new.json*', route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify(WHATS_NEW)})
  );
  await openPage(page, path);
}

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

    test("More marks What's new until it is opened, and shows the version", async ({page}) => {
      await openWithStaleSeen(page, '/more');

      const row = page.getByTestId('more-whats-new');
      await expect(row.locator('.cmn-badge-indicator')).toBeVisible();
      await expect(page.getByTestId('more-version')).toContainText('Version ');

      await row.click();
      const panel = page.getByTestId('whats-new-list');
      await expect(panel).toContainText('You can show only money in or only money out.');
      await expect(panel.getByText('Every change in this version').first()).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(row.locator('.cmn-badge-indicator')).toHaveCount(0);
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

  test("the avatar menu opens What's new", async ({page}) => {
    await openWithStaleSeen(page, '/dashboard');

    await page.getByRole('button', {name: 'Account menu'}).click();
    await page.getByRole('menuitem', {name: "What's new"}).click();
    await expect(page.getByTestId('whats-new-list')).toContainText('only money in');
  });
});
