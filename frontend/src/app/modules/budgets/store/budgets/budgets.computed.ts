import {computed, type Signal} from '@angular/core';

import {type BudgetSummaryItem} from '../../models/budget/budget.model';

const PCT_MAX = 100;

interface StateSignals {
  summaryItems: Signal<BudgetSummaryItem[]>;
  editingId: Signal<Nullable<string>>;
  selectedYear: Signal<number>;
  selectedMonth: Signal<number>;
}

export function budgetsComputed(store: StateSignals) {
  return {
    totalSpent: computed(() => store.summaryItems().reduce((s, b) => s + b.spent, 0)),
    totalBudget: computed(() => store.summaryItems().reduce((s, b) => s + b.monthlyLimit, 0)),
    overBudgetCount: computed(() => store.summaryItems().filter(b => b.isOverBudget).length),
    overallPct: computed(() => {
      const total = store.summaryItems().reduce((s, b) => s + b.monthlyLimit, 0);
      const spent = store.summaryItems().reduce((s, b) => s + b.spent, 0);
      return total > 0 ? Math.min((spent / total) * PCT_MAX, PCT_MAX) : 0;
    }),
    selectedMonthDate: computed(() => new Date(store.selectedYear(), store.selectedMonth() - 1, 1)),
    monthName: computed(() =>
      new Date(store.selectedYear(), store.selectedMonth() - 1, 1).toLocaleDateString('en-US', {
        month: 'long',
      })
    ),
  };
}
