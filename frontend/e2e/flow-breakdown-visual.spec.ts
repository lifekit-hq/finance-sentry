import {expect} from '@playwright/test';

import {describeVisualBaseline, mockGet} from './support/visual-baseline';

function item(
  id: string,
  description: string,
  date: string,
  amountUsd: number,
  bucket: string,
  category: string | null
): Record<string, unknown> {
  return {
    transactionId: id,
    accountId: 'acc-monzo',
    bankName: 'Monzo',
    accountLast4: '4821',
    currency: 'USD',
    amount: amountUsd,
    amountUsd,
    date,
    description,
    merchantName: description,
    category,
    direction: amountUsd >= 0 ? 'in' : 'out',
    bucket,
    counterpartyName: null,
    flowRole: null,
  };
}

const BREAKDOWN = {
  month: '2026-09',
  items: [
    item('f-1', 'Salary — Acme Ltd', '2026-09-14T06:00:00Z', 3200, 'income', 'INCOME'),
    item('f-2', 'Rent', '2026-09-01T09:00:00Z', -1450, 'spending', 'RENT'),
    item('f-3', 'Tesco Groceries', '2026-09-14T17:40:00Z', -62.18, 'spending', 'GROCERIES'),
    item('f-4', 'Interactive Brokers', '2026-09-05T10:00:00Z', -600, 'invested', null),
    item('f-5', 'Transfer to Savings', '2026-09-03T10:00:00Z', -500, 'excluded-transfer', null),
  ],
};

// The whole-book cards moved here from Home (#1186): the bridge over the month, the outlook and FIRE.
const BRIDGE = {
  from: '2026-09-01',
  to: '2026-09-15',
  startValue: 49_900,
  endValue: 50_000,
  savings: 300,
  investmentReturn: -150,
  market: -120,
  income: 10,
  fees: 40,
  taxes: 0,
  fxTranslation: -80,
  revaluation: 0,
  newlyLinked: 0,
  unlinked: 0,
  residual: 30,
  residualAboveTolerance: false,
  quality: 'COMPLETE',
  reasons: [],
  currency: 'USD',
};

const PROJECTION = {
  status: 'Projected',
  horizonMonths: 12,
  currentNetWorth: 50_000,
  monthlySavings: 1_000,
  completeMonths: 3,
  projectedNetWorth: {low: 63_100, central: 64_600, high: 66_100},
  realAnnualReturnRange: {low: 0.02, central: 0.05, high: 0.08},
  hasStaleSleeves: false,
  baseCurrency: 'USD',
};

const INCOME_LENS = {
  status: 'Shown',
  netWorth: 50_000,
  safeWithdrawalRate: 0.04,
  monthlyIncome: 166.67,
  hasStaleSleeves: false,
  baseCurrency: 'USD',
};

const FIRE = {
  status: 'Projected',
  target: 930_000,
  currentNetWorth: 50_000,
  monthlySavings: 1_000,
  annualSpend: 37_200,
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
  projectedDate: '2051-03-01',
  monthsToFire: 294,
  hasStaleSleeves: false,
  baseCurrency: 'USD',
  realAnnualReturnRange: {low: 0.02, central: 0.05, high: 0.08},
  monthsToFireRange: {low: 240, central: 294, high: 380},
  earliestDate: '2046-09-01',
  latestDate: '2058-05-01',
  completeMonths: 3,
};

describeVisualBaseline({
  name: 'flow-breakdown',
  title: 'Flow breakdown',
  path: '/dashboard/breakdown?month=2026-09',
  mocks: async page => {
    await mockGet(page, 'dashboard/flow-breakdown**', BREAKDOWN);
    await mockGet(page, 'net-worth/bridge**', BRIDGE);
    await mockGet(page, 'wealth/projection', PROJECTION);
    await mockGet(page, 'wealth/income-lens', INCOME_LENS);
    await mockGet(page, 'wealth/fire', FIRE);
  },
  ready: async page => {
    await expect(page.getByText('Tesco Groceries').locator('visible=true').first()).toBeVisible();
    await expect(page.getByTestId('net-worth-bridge-end')).toBeVisible();
    await expect(page.getByTestId('net-worth-projection')).toBeVisible();
    await expect(page.getByTestId('fire-assumptions')).toBeVisible();
  },
});
