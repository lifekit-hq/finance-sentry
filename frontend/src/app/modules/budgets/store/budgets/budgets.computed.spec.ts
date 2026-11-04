import {signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {type BudgetSummaryResponse} from '../../models/budget/budget.model';
import {budgetsComputed} from './budgets.computed';

const SUMMARY: BudgetSummaryResponse = {
  year: 2027,
  month: 4,
  items: [
    {
      overrideId: null,
      category: 'FOOD_AND_DRINK',
      categoryLabel: 'Food & drink',
      estimate: 100,
      suggestedAmount: 100,
      source: 'history',
      spent: 150,
      remaining: -50,
      isOverBudget: true,
      currency: 'USD',
      paceRatio: 1.5,
      projectedMonthEndSpend: 150,
      isOffPace: true,
      expectedMonthEnd: 150,
    },
  ],
  totalEstimate: 200,
  totalSpent: 150,
  expectedMonthEnd: 260,
  hasEnoughHistory: false,
  historyMonthsUsed: 2,
  minHistoryMonths: 3,
  skippedMonths: [{month: '2026-10', reason: 'excluded'}],
};

function computedFor(summary: Nullable<BudgetSummaryResponse>, year = 2027, month = 4) {
  return budgetsComputed({
    summary: signal(summary),
    selectedYear: signal(year),
    selectedMonth: signal(month),
  });
}

describe('budgetsComputed', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2027, 3, 15));
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('reads the month from the summary', () => {
    const c = computedFor(SUMMARY);

    expect(c.items()).toHaveLength(1);
    expect(c.totalEstimate()).toBe(200);
    expect(c.totalSpent()).toBe(150);
    expect(c.expectedMonthEnd()).toBe(260);
    expect(c.overBudgetCount()).toBe(1);
    expect(c.overallPct()).toBe(75);
    expect(c.hasEnoughHistory()).toBe(false);
    expect(c.historyMonthsUsed()).toBe(2);
    expect(c.minHistoryMonths()).toBe(3);
  });

  it('turns the skipped months into a plain-words note', () => {
    expect(computedFor(SUMMARY).skippedNote()).toContain('October 2026 is left out');
  });

  it('is empty, with no note, before the first month arrives', () => {
    const c = computedFor(null);

    expect(c.items()).toEqual([]);
    expect(c.totalEstimate()).toBe(0);
    expect(c.skippedNote()).toBeNull();
    expect(c.overBudgetCount()).toBe(0);
    expect(c.overallPct()).toBe(0);
  });

  it('knows whether the selected month is the current one', () => {
    expect(computedFor(SUMMARY, 2027, 4).isCurrentMonth()).toBe(true);
    expect(computedFor(SUMMARY, 2027, 3).isCurrentMonth()).toBe(false);
  });

  it('names the selected month', () => {
    const c = computedFor(SUMMARY, 2027, 3);

    expect(c.monthName()).toBe('March');
    expect(c.selectedMonthDate()).toEqual(new Date(2027, 2, 1));
  });
});
