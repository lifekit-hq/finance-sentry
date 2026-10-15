import {expect, test} from '@playwright/test';

import {BROKERAGE_GROWTH} from './support/accounts-mocks';
import {mockApis} from './support/dossier-mocks';
import {mockGet} from './support/visual-baseline';

test.describe('Investments contributions vs growth', () => {
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
