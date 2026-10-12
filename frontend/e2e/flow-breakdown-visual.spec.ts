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

describeVisualBaseline({
  name: 'flow-breakdown',
  title: 'Flow breakdown',
  path: '/dashboard/breakdown?month=2026-09',
  mocks: async page => {
    await mockGet(page, 'dashboard/flow-breakdown**', BREAKDOWN);
  },
  ready: async page => {
    await expect(page.getByText('Tesco Groceries').locator('visible=true').first()).toBeVisible();
  },
});
