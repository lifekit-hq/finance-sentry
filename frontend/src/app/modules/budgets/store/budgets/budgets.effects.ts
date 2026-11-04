import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {
  type BudgetSummaryResponse,
  type SaveOverrideRequest,
} from '../../models/budget/budget.model';
import {BudgetsService} from '../../services/budgets.service';

interface EffectsStore {
  setSummary: (summary: BudgetSummaryResponse) => void;
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
  clearRefreshFailed: () => void;
  setRefreshFailed: () => void;
  setEditing: (category: Nullable<string>) => void;
  setSelectedPeriod: (year: number, month: number) => void;
  selectedYear: () => number;
  selectedMonth: () => number;
}

export function budgetsEffects(store: EffectsStore) {
  const service = inject(BudgetsService);

  const fetchSummary = () =>
    service.getBudgetSummary(store.selectedYear(), store.selectedMonth()).pipe(
      tap(res => {
        store.setSummary(res);
        store.setSuccess();
      })
    );

  const loadMonth = () =>
    fetchSummary().pipe(
      catchError(() => {
        store.setError(null);
        return EMPTY;
      })
    );

  // The estimates are recomputed on the server (an override changes the month's total and pace),
  // so every change is followed by a reload instead of a local patch.
  const refreshAfterChange = () =>
    fetchSummary().pipe(
      catchError(() => {
        store.setRefreshFailed();
        return EMPTY;
      })
    );

  return {
    loadSummary: rxMethod<void>(
      pipe(
        tap(() => {
          store.clearRefreshFailed();
          store.setLoading();
        }),
        switchMap(() => loadMonth())
      )
    ),

    // A hand edit is an override: created the first time, updated after that.
    saveOverride: rxMethod<SaveOverrideRequest>(
      pipe(
        switchMap(({category, overrideId, amount}) =>
          (overrideId === null
            ? service.createBudget({category, monthlyLimit: amount})
            : service.updateBudget(overrideId, {monthlyLimit: amount})
          ).pipe(
            tap(() => store.setEditing(null)),
            switchMap(() => refreshAfterChange()),
            catchError(() => EMPTY)
          )
        )
      )
    ),

    // Reset to suggested: drop the override, the line returns to its suggestion.
    resetOverride: rxMethod<string>(
      pipe(
        switchMap(overrideId =>
          service.deleteBudget(overrideId).pipe(
            switchMap(() => refreshAfterChange()),
            catchError(() => EMPTY)
          )
        )
      )
    ),

    navigateToPeriod: rxMethod<{year: number; month: number}>(
      pipe(
        tap(({year, month}) => {
          store.setSelectedPeriod(year, month);
          store.clearRefreshFailed();
          store.setLoading();
        }),
        switchMap(() => loadMonth())
      )
    ),
  };
}

export function budgetsHooks(store: {loadSummary: () => void}): void {
  store.loadSummary();
}
