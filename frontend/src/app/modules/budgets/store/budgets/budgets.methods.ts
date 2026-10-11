import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type BudgetSummaryItem} from '../../models/budget/budget.model';
import {type BudgetsState} from './budgets.state';

export function budgetsMethods(store: WritableStateSource<BudgetsState>) {
  return {
    setSummaryItems(summaryItems: BudgetSummaryItem[]): void {
      patchState(store, {summaryItems, status: 'idle', refreshFailed: false});
    },
    setEditing(id: Nullable<string>): void {
      patchState(store, {editingId: id});
    },
    setLoading(): void {
      patchState(store, {status: 'loading', refreshFailed: false});
    },
    setError(): void {
      patchState(store, {status: 'error'});
    },
    setRefreshFailed(): void {
      patchState(store, {refreshFailed: true});
    },
    updateBudgetInList(id: string, monthlyLimit: number): void {
      patchState(store, (s: BudgetsState) => ({
        summaryItems: s.summaryItems.map(b =>
          b.id === id
            ? {
                ...b,
                monthlyLimit,
                remaining: monthlyLimit - b.spent,
                isOverBudget: b.spent > monthlyLimit,
              }
            : b
        ),
        editingId: null,
      }));
    },
    removeBudget(id: string): void {
      patchState(store, (s: BudgetsState) => ({
        summaryItems: s.summaryItems.filter(b => b.id !== id),
      }));
    },
    setSelectedPeriod(year: number, month: number): void {
      patchState(store, {selectedYear: year, selectedMonth: month});
    },
  };
}
