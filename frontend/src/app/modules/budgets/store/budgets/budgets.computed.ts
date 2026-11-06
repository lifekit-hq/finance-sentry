import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {APP_LOCALE} from '../../../../shared/constants/date/date.constants';
import {type BudgetSummaryResponse} from '../../models/budget/budget.model';
import {BudgetEstimateUtils} from '../../utils/budget-estimate.utils';

interface StateSignals {
  summary: Signal<Nullable<BudgetSummaryResponse>>;
  selectedYear: Signal<number>;
  selectedMonth: Signal<number>;
}

export function budgetsComputed(store: StateSignals) {
  return {
    items: computed(() => store.summary()?.items ?? []),
    totalEstimate: computed(() => store.summary()?.totalEstimate ?? 0),
    totalSpent: computed(() => store.summary()?.totalSpent ?? 0),
    expectedMonthEnd: computed(() => store.summary()?.expectedMonthEnd ?? 0),
    hasEnoughHistory: computed(() => store.summary()?.hasEnoughHistory ?? true),
    historyMonthsUsed: computed(() => store.summary()?.historyMonthsUsed ?? 0),
    minHistoryMonths: computed(() => store.summary()?.minHistoryMonths ?? 0),
    skippedNote: computed(() =>
      BudgetEstimateUtils.skippedNote(store.summary()?.skippedMonths ?? [])
    ),
    spendIncomplete: computed(() => store.summary()?.spendIncomplete ?? false),
    overBudgetCount: computed(
      () => (store.summary()?.items ?? []).filter(b => b.isOverBudget).length
    ),
    overallPct: computed(() =>
      BudgetEstimateUtils.usedPct(
        store.summary()?.totalSpent ?? 0,
        store.summary()?.totalEstimate ?? 0
      )
    ),
    isCurrentMonth: computed(() => {
      const now = new Date();
      return (
        store.selectedYear() === now.getFullYear() && store.selectedMonth() === now.getMonth() + 1
      );
    }),
    selectedMonthDate: computed(() => new Date(store.selectedYear(), store.selectedMonth() - 1, 1)),
    monthName: computed(() =>
      new Date(store.selectedYear(), store.selectedMonth() - 1, 1).toLocaleDateString(APP_LOCALE, {
        month: 'long',
      })
    ),
  };
}
