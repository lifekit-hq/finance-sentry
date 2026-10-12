import {inject} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type BudgetSummaryItem} from '../../models/budget/budget.model';
import {BudgetsService} from '../../services/budgets.service';

interface EffectsStore {
  setSummaryItems: (items: BudgetSummaryItem[]) => void;
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
  clearRefreshFailed: () => void;
  setRefreshFailed: () => void;
  setEditing: (id: Nullable<string>) => void;
  updateBudgetInList: (id: string, limit: number) => void;
  removeBudget: (id: string) => void;
  setSelectedPeriod: (year: number, month: number) => void;
  selectedYear: () => number;
  selectedMonth: () => number;
}

export function budgetsEffects(store: EffectsStore) {
  const service = inject(BudgetsService);

  const fetchSummary = () =>
    service.getBudgetSummary(store.selectedYear(), store.selectedMonth()).pipe(
      tap(res => {
        store.setSummaryItems(res.items);
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

    create: rxMethod<{category: string; monthlyLimit: number}>(
      pipe(
        switchMap(req =>
          service.createBudget(req).pipe(
            switchMap(() => refreshAfterChange()),
            catchError(() => EMPTY)
          )
        )
      )
    ),

    update: rxMethod<{id: string; monthlyLimit: number}>(
      pipe(
        switchMap(({id, monthlyLimit}) =>
          service.updateBudget(id, {monthlyLimit}).pipe(
            tap(() => store.updateBudgetInList(id, monthlyLimit)),
            switchMap(() => refreshAfterChange()),
            catchError(() => EMPTY)
          )
        )
      )
    ),

    remove: rxMethod<string>(
      pipe(
        switchMap(id =>
          service.deleteBudget(id).pipe(
            tap(() => store.removeBudget(id)),
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
