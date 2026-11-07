import {expect} from '@playwright/test';

import {mockGet, type VisualScreen} from '../visual-baseline';

function item(
  category: string,
  categoryLabel: string,
  estimate: number,
  spent: number,
  isOffPace: boolean,
  overrideId: string | null = null
): Record<string, unknown> {
  return {
    overrideId,
    category,
    categoryLabel,
    estimate,
    suggestedAmount: overrideId === null ? estimate : estimate - 40,
    source: overrideId === null ? 'history' : 'override',
    spent,
    remaining: estimate - spent,
    isOverBudget: spent > estimate,
    currency: 'USD',
    paceRatio: spent / estimate,
    projectedMonthEndSpend: spent * 2,
    isOffPace,
    expectedMonthEnd: Math.max(estimate, spent * 2),
  };
}

// 2026-11, halfway through the month: groceries on pace, dining off pace, transport over, a hand-set
// entertainment amount. September and October are left out of the history (the card moved off the link).
const SUMMARY = {
  year: 2026,
  month: 11,
  items: [
    item('GROCERIES', 'Groceries', 600, 280, false),
    item('FOOD_AND_DRINK', 'Dining out', 250, 190, true),
    item('TRANSPORT', 'Transport', 120, 135, true),
    item('ENTERTAINMENT', 'Entertainment', 80, 22, false, 'o-1'),
  ],
  totalEstimate: 1050,
  totalSpent: 627,
  expectedMonthEnd: 1180,
  hasEnoughHistory: true,
  historyMonthsUsed: 4,
  minHistoryMonths: 3,
  skippedMonths: [
    {month: '2026-09', reason: 'excluded'},
    {month: '2026-10', reason: 'excluded'},
  ],
};

export const BUDGETS_SCREEN: VisualScreen = {
  name: 'budgets',
  title: 'Monthly estimates',
  path: '/budgets',
  mocks: async page => {
    await mockGet(page, 'budgets/summary**', SUMMARY);
  },
  ready: async page => {
    await expect(page.getByText('Dining out').first()).toBeVisible();
  },
};
