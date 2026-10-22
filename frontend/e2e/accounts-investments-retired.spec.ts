import {expect, test} from '@playwright/test';

import {mockAccounts} from './support/accounts-mocks';
import {mockApis} from './support/dossier-mocks';

// The Investments tab is retired: Accounts is the one inventory, and old links land on it.
const SHOTS_DIR = process.env['RETIRED_INVESTMENTS_SHOTS'];

test.describe('Retired Investments tab', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
    await mockAccounts(page);
  });

  for (const from of ['/accounts/investments', '/investments']) {
    test(`${from} redirects to Accounts`, async ({page}) => {
      await page.goto(from);

      await expect(page).toHaveURL(/\/accounts\/list$/);
      await expect(page.getByRole('navigation', {name: 'Accounts view'})).toHaveCount(0);
    });
  }

  test('?provider= opens that institution accordion, scrolled into view, with P&L on its rows', async ({
    page,
  }) => {
    await page.goto('/accounts/investments?provider=ibkr');

    await expect(page).toHaveURL(/\/accounts\/list\?provider=ibkr$/);
    const broker = page.locator('cmn-disclosure-row', {hasText: 'Interactive Brokers'});
    await expect(broker.locator('button[aria-controls]').first()).toHaveAttribute(
      'aria-expanded',
      'true'
    );
    await expect(broker.getByTestId('position-row').first()).toBeInViewport();
    await expect(broker.getByTestId('position-pnl').first()).toContainText('%');
    await expect(
      page
        .locator('cmn-disclosure-row', {hasText: 'Binance'})
        .locator('button[aria-controls]')
        .first()
    ).toHaveAttribute('aria-expanded', 'false');
    if (SHOTS_DIR) {
      await page.screenshot({path: `${SHOTS_DIR}/accounts-provider-deep-link.png`});
    }
  });

  test('the command palette no longer lists Investments', async ({page}) => {
    await page.goto('/accounts/list');
    await expect(page.locator('cmn-disclosure-row').first()).toBeVisible();
    await page.keyboard.press('Control+k');

    const palette = page.getByRole('dialog');
    await expect(palette.getByText('Transactions')).toBeVisible();
    await expect(palette.getByText('Investments')).toHaveCount(0);
  });
});
