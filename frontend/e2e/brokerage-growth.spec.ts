import {expect, test} from '@playwright/test';

import {BROKERAGE_GROWTH, BROKERAGE_PNL} from './support/accounts-mocks';
import {mockHome} from './support/home-mocks';
import {mockGet} from './support/visual-baseline';

test.describe('Accounts brokerage and crypto sections: one performance card each', () => {
  test('splits the brokerage change into what was paid in and what it earned', async ({page}) => {
    await mockHome(page);
    await mockGet(page, 'wealth/brokerage/growth', BROKERAGE_GROWTH);
    await page.goto('/accounts/list');

    const section = page.getByTestId('brokerage-growth');
    await expect(section).toContainText('since 30 Jun 2026');
    await expect(page.getByTestId('brokerage-growth-contributed')).toContainText('+$4,000.00');
    await expect(page.getByTestId('brokerage-growth-growth')).toContainText('+$3,300.00');
    await expect(page.getByTestId('brokerage-growth-growth')).toContainText('+9.9%');
    await expect(page.getByTestId('brokerage-growth-value-now')).toBeVisible();
  });

  test('names the gap between the investments return and the money return as timing', async ({
    page,
  }) => {
    await mockHome(page);
    await mockGet(page, 'wealth/brokerage/growth', BROKERAGE_GROWTH);
    await page.goto('/accounts/list');

    await page
      .getByTestId('brokerage-growth')
      .getByRole('button', {name: 'How this is computed'})
      .click();
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
    await expect(page.getByTestId('crypto-growth-contributed')).toContainText('+$500.00');
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

  test('adds QQQ over the same dates to the book against SPY line', async ({page}) => {
    await mockHome(page);
    await page.goto('/accounts/list');

    await expect(page.getByTestId('book-vs-qqq-returns')).toHaveText('QQQ +18.7%');
  });

  test('puts realized and unrealized P&L on the capital in the window, with the honest label for a partial basis', async ({
    page,
  }) => {
    await mockHome(page);
    await page.goto('/accounts/list');

    await expect(page.getByTestId('brokerage-pnl-net-deposited-amount')).toHaveText('$35,200.00');
    await expect(page.getByTestId('brokerage-pnl-net-deposited')).toContainText('Capital in the window');
    await expect(page.getByTestId('brokerage-pnl-net-deposited-detail')).toContainText(
      'already includes earlier market gains'
    );
    await expect(page.getByTestId('brokerage-pnl-realized-amount')).toContainText('+$1,240.00');
    await expect(page.getByTestId('brokerage-pnl-realized-amount')).toContainText('+3.5%');
    const unrealized = page.getByTestId('brokerage-pnl-unrealized');
    await expect(unrealized).toContainText('+$2,310.00');
    await expect(unrealized).toContainText('Partial: 2 of 3 · 78% of value');
    await expect(page.getByTestId('brokerage-pnl-unrealized-detail')).toContainText(
      'KYIV is left out'
    );
    await expect(page.getByTestId('brokerage-pnl-unrealized-amount')).not.toContainText('%');
  });

  test('shows "Basis unavailable" for unrealized instead of a number when no basis is verified', async ({
    page,
  }) => {
    await mockHome(page);
    await mockGet(page, 'wealth/brokerage/pnl', {
      ...BROKERAGE_PNL,
      unrealized: {
        usd: null,
        percentOfNetDeposited: null,
        status: 'UNAVAILABLE',
        measured: 0,
        total: 3,
        coverage: null,
        unavailableSymbols: ['AAA', 'BBB', 'KYIV'],
      },
    });
    await page.goto('/accounts/list');

    await expect(page.getByTestId('brokerage-pnl-unrealized-amount')).toHaveText(
      'Basis unavailable'
    );
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
