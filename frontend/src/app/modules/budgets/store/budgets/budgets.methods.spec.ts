import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {type BudgetSummaryResponse} from '../../models/budget/budget.model';
import {budgetsMethods} from './budgets.methods';
import {initialBudgetsState} from './budgets.state';

const SUMMARY: BudgetSummaryResponse = {
  year: 2027,
  month: 4,
  items: [],
  totalEstimate: 100,
  totalSpent: 10,
  expectedMonthEnd: 100,
  hasEnoughHistory: true,
  historyMonthsUsed: 6,
  minHistoryMonths: 3,
  skippedMonths: [],
};

function setup() {
  const state = signalState(initialBudgetsState);
  return {state, methods: budgetsMethods(state)};
}

describe('budgetsMethods refresh failure', () => {
  it('flags a failed refresh without touching the month', () => {
    const {state, methods} = setup();
    methods.setSummary(SUMMARY);

    methods.setRefreshFailed();

    expect(state.refreshFailed()).toBe(true);
    expect(state.summary()).toEqual(SUMMARY);
  });

  it('clears the flag when a month load starts', () => {
    const {state, methods} = setup();
    methods.setRefreshFailed();

    methods.clearRefreshFailed();

    expect(state.refreshFailed()).toBe(false);
  });

  it('clears the flag when a summary arrives', () => {
    const {state, methods} = setup();
    methods.setRefreshFailed();

    methods.setSummary(SUMMARY);

    expect(state.refreshFailed()).toBe(false);
  });
});

describe('budgetsMethods period and editing', () => {
  it('drops the previous month when another one is selected', () => {
    const {state, methods} = setup();
    methods.setSummary(SUMMARY);

    methods.setSelectedPeriod(2027, 3);

    expect(state.selectedYear()).toBe(2027);
    expect(state.selectedMonth()).toBe(3);
    expect(state.summary()).toBeNull();
  });

  it('tracks the line being edited', () => {
    const {state, methods} = setup();

    methods.setEditing('FOOD_AND_DRINK');
    expect(state.editingCategory()).toBe('FOOD_AND_DRINK');

    methods.setEditing(null);
    expect(state.editingCategory()).toBeNull();
  });
});
