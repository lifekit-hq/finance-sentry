import {expect} from '@playwright/test';

import {describeVisualBaseline, mockGet} from './support/visual-baseline';

function item(
  id: string,
  category: string,
  categoryLabel: string,
  limit: number,
  spent: number,
  isOffPace: boolean
): Record<string, unknown> {
  return {
    id,
    category,
    categoryLabel,
    monthlyLimit: limit,
    spent,
    remaining: limit - spent,
    isOverBudget: spent > limit,
    currency: 'USD',
    paceRatio: spent / limit,
    projectedMonthEndSpend: spent * 2,
    isOffPace,
  };
}

// 2026-09, halfway through the month: groceries on pace, dining off pace, transport over.
const SUMMARY = {
  year: 2026,
  month: 9,
  items: [
    item('b-1', 'GROCERIES', 'Groceries', 600, 280, false),
    item('b-2', 'FOOD_AND_DRINK', 'Dining out', 250, 190, true),
    item('b-3', 'TRANSPORT', 'Transport', 120, 135, true),
    item('b-4', 'ENTERTAINMENT', 'Entertainment', 80, 22, false),
  ],
  totalLimit: 1050,
  totalSpent: 627,
};

describeVisualBaseline({
  name: 'budgets',
  title: 'Budgets',
  path: '/budgets',
  mocks: async page => {
    await mockGet(page, 'budgets/summary**', SUMMARY);
  },
  ready: async page => {
    await expect(page.getByText('Dining out').first()).toBeVisible();
  },
});
