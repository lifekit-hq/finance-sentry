import {expect, test} from '@playwright/test';

import {BOOK_PERFORMANCE, BROKERAGE_GROWTH, CRYPTO_GROWTH} from './support/accounts-mocks';
import {mockApis} from './support/dossier-mocks';
import {mockGet} from './support/visual-baseline';

test.describe('Investments contributions vs growth', () => {
  test.beforeEach(async ({page}) => {
    await mockGet(page, 'wealth/crypto/growth', CRYPTO_GROWTH);
    await mockGet(page, 'radar/book-performance', BOOK_PERFORMANCE);
  });

  test('splits the brokerage change into what was paid in and what it earned', async ({page}) => {
    await mockApis(page);
    await mockGet(page, 'wealth/brokerage/growth', BROKERAGE_GROWTH);
    await page.goto('/accounts/investments');

    const section = page.getByTestId('brokerage-growth');
    await expect(section).toContainText('since 30 Jun 2026');
    await expect(page.getByTestId('brokerage-growth-contributed')).toHaveText('+$4,000.00');
    await expect(page.getByTestId('brokerage-growth-growth')).toContainText('+$3,300.00');
    await expect(page.getByTestId('brokerage-growth-growth')).toContainText('(+9.9%)');
  });

  test('names the gap between the investments return and the money return as timing', async ({
    page,
  }) => {
    await mockApis(page);
    await mockGet(page, 'wealth/brokerage/growth', BROKERAGE_GROWTH);
    await page.goto('/accounts/investments');

    await expect(page.getByTestId('brokerage-growth-timing-gap')).toHaveText(
      'Your investments returned +7.1% (time-weighted); your money earned +9.9% ' +
        '(money-weighted). The difference, 2.8 points, is the timing of your deposits and ' +
        'withdrawals.'
    );
  });

  test('splits the crypto change on its own card', async ({page}) => {
    await mockApis(page);
    await mockGet(page, 'wealth/brokerage/growth', BROKERAGE_GROWTH);
    await page.goto('/accounts/investments');

    await expect(page.getByTestId('crypto-growth-since')).toContainText(
      'Binance and Revolut X since 30 Jun 2026'
    );
    await expect(page.getByTestId('crypto-growth-contributed')).toHaveText('+$500.00');
    await expect(page.getByTestId('crypto-growth-growth')).toContainText('(+3.5%)');
    await expect(page.getByTestId('crypto-growth-timing-gap')).toHaveCount(0);
  });

  test('puts 10,000 into the book and into SPY over the longest measured window', async ({
    page,
  }) => {
    await mockApis(page);
    await mockGet(page, 'wealth/brokerage/growth', BROKERAGE_GROWTH);
    await page.goto('/accounts/investments');

    await expect(page.getByTestId('growth-of-ten-thousand-since')).toHaveText(
      /Put in on 14 Sept? 2025, worth on 14 Sept? 2026\./
    );
    await expect(page.getByTestId('growth-of-ten-thousand-book')).toHaveText('$11,251.00 (+12.5%)');
    await expect(page.getByTestId('growth-of-ten-thousand-spy')).toHaveText('$11,532.00 (+15.3%)');
  });

  test('says no contributions are recorded yet instead of reading every deposit as growth', async ({
    page,
  }) => {
    await mockApis(page);
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
    await page.goto('/accounts/investments');

    await expect(page.getByTestId('brokerage-growth-no-flows')).toContainText(
      'No contributions recorded yet for Interactive Brokers.'
    );
    await expect(page.getByTestId('brokerage-growth-no-flows')).toContainText(
      'Deposits & Withdrawals'
    );
    await expect(page.getByTestId('brokerage-growth-split')).toHaveCount(0);
  });
});
