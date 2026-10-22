import {expect, test} from '@playwright/test';

import {BROKERAGE_GROWTH} from './support/accounts-mocks';
import {mockHome} from './support/home-mocks';
import {mockGet} from './support/visual-baseline';

test.describe('Accounts brokerage and crypto sections: one performance card each', () => {
  test('splits the brokerage change into what was paid in and what it earned', async ({page}) => {
    await mockHome(page);
    await mockGet(page, 'wealth/brokerage/growth', BROKERAGE_GROWTH);
    await page.goto('/accounts/list');

    const section = page.getByTestId('brokerage-growth');
    await expect(section).toContainText('since 30 Jun 2026');
    await expect(page.getByTestId('brokerage-growth-contributed')).toContainText('$4,000.00');
    await expect(page.getByTestId('brokerage-growth-growth')).toContainText('$3,300.00');
    await expect(page.getByTestId('brokerage-growth-growth')).toContainText('+9.9%');
    await expect(page.getByTestId('brokerage-growth-value-now')).toBeVisible();
  });

  test('names the gap between the investments return and the money return as timing', async ({
    page,
  }) => {
    await mockHome(page);
    await mockGet(page, 'wealth/brokerage/growth', BROKERAGE_GROWTH);
    await page.goto('/accounts/list');

    await page.getByTestId('brokerage-growth').locator('summary').click();
    await expect(page.getByTestId('brokerage-growth-timing-gap')).toHaveText(
      'Your investments returned +7.1% (time-weighted); your money earned +9.9% ' +
        '(money-weighted). The difference, 2.8 points, is the timing of your deposits and ' +
        'withdrawals.'
    );
  });

  test('splits the crypto change on its own card', async ({page}) => {
    await mockHome(page);
    await mockGet(page, 'wealth/brokerage/growth', BROKERAGE_GROWTH);
    await page.goto('/accounts/list');

    await expect(page.getByTestId('crypto-growth-since')).toContainText('since 30 Jun 2026');
    await expect(page.getByTestId('crypto-growth-contributed')).toContainText('$500.00');
    await expect(page.getByTestId('crypto-growth-growth')).toContainText('+3.5%');
    await expect(page.getByTestId('crypto-growth').locator('fns-book-vs-spy')).toHaveCount(0);
    await expect(page.getByTestId('crypto-growth-timing-gap')).toHaveCount(0);
  });

  test('puts the book against SPY on one line inside the brokerage card', async ({page}) => {
    await mockHome(page);
    await mockGet(page, 'wealth/brokerage/growth', BROKERAGE_GROWTH);
    await page.goto('/accounts/list');

    const line = page.getByTestId('brokerage-growth').getByTestId('book-vs-spy');
    await expect(line).toContainText(/since 14 Sept? 2025/);
    await expect(page.getByTestId('book-vs-spy-returns')).toHaveText('+12.5% vs +15.3%');
  });

  test('says no contributions are recorded yet instead of reading every deposit as growth', async ({
    page,
  }) => {
    await mockHome(page);
    await mockGet(page, 'wealth/brokerage/growth', {
      ...BROKERAGE_GROWTH,
      contributed: null,
      newlyLinked: null,
      growth: null,
      income: null,
      fees: null,
      taxes: null,
      moneyWeightedReturn: null,
      sourcesWithoutFlows: ['ibkr'],
      headline: {
        ...BROKERAGE_GROWTH.headline,
        amount: null,
        percent: null,
        quality: 'UNAVAILABLE',
        reasons: [{code: 'UNKNOWN_FLOW', source: 'ibkr'}],
      },
    });
    await page.goto('/accounts/list');

    await expect(page.getByTestId('brokerage-growth-no-flows')).toContainText(
      'No contributions recorded yet for Interactive Brokers.'
    );
    await expect(page.getByTestId('brokerage-growth-no-flows')).toContainText(
      'Deposits & Withdrawals'
    );
    await expect(page.getByTestId('brokerage-growth-split')).toHaveCount(0);
  });
});
