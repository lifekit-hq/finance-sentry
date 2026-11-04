import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type BudgetSummaryResponse} from '../../models/budget/budget.model';
import {type BudgetsState} from './budgets.state';

export function budgetsMethods(store: WritableStateSource<BudgetsState>) {
  return {
    setSummary(summary: BudgetSummaryResponse): void {
      patchState(store, {summary, refreshFailed: false});
    },
    setEditing(category: Nullable<string>): void {
      patchState(store, {editingCategory: category});
    },
    clearRefreshFailed(): void {
      patchState(store, {refreshFailed: false});
    },
    setRefreshFailed(): void {
      patchState(store, {refreshFailed: true});
    },
    setSelectedPeriod(year: number, month: number): void {
      patchState(store, {selectedYear: year, selectedMonth: month, summary: null});
    },
  };
}
