import {inject, type Signal, untracked} from '@angular/core';
import {toObservable} from '@angular/core/rxjs-interop';
import {extractErrorCode} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap, timer} from 'rxjs';

import {
  type DashboardData,
  type HistoryRange,
  type NetWorthScopeChangeDto,
  type NetWorthSnapshotDto,
} from '../../models/dashboard/dashboard.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {DashboardRangeUtils} from '../../utils/dashboard-range.utils';

interface EffectsStore {
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
  setData: (data: DashboardData) => void;
  setNetWorthHistory: (snapshots: NetWorthSnapshotDto[]) => void;
  setHistoryLoading: (loading: boolean) => void;
  setHistoryError: (error: string | null) => void;
  setHistoryHasHistory: (hasHistory: boolean) => void;
  setHistoryScopeChanges: (scopeChanges: NetWorthScopeChangeDto[]) => void;
  historyRange: Signal<HistoryRange>;
}

const MINUTES_PER_REFRESH = 5;
const SECONDS_PER_MINUTE = 60;
const MS_PER_SECOND = 1000;
const REFRESH_INTERVAL_MS = MINUTES_PER_REFRESH * SECONDS_PER_MINUTE * MS_PER_SECOND;

export function dashboardEffects(store: EffectsStore) {
  const bankSyncService = inject(BankSyncService);

  return {
    // The selected range scopes the month-bucketed widgets too (income vs spending,
    // savings rate, top categories) — one range, one story across the whole dashboard.
    load: rxMethod<HistoryRange>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(range =>
          bankSyncService
            .getDashboardData(
              DashboardRangeUtils.months(range),
              DashboardRangeUtils.windowMonths(range),
              DashboardRangeUtils.windowFrom(range)
            )
            .pipe(
              tap(data => {
                store.setData(data);
                store.setSuccess();
              }),
              catchError((err: unknown) => {
                store.setError(extractErrorCode(err));
                return EMPTY;
              })
            )
        )
      )
    ),

    loadNetWorthHistory: rxMethod<HistoryRange>(
      pipe(
        tap(() => store.setHistoryLoading(true)),
        switchMap(range =>
          bankSyncService.getNetWorthHistory(range).pipe(
            tap(response => {
              store.setNetWorthHistory(response.snapshots);
              store.setHistoryHasHistory(response.hasHistory);
              store.setHistoryScopeChanges(response.scopeChanges ?? []);
              store.setHistoryLoading(false);
              store.setHistoryError(null);
            }),
            catchError((err: unknown) => {
              store.setHistoryError(extractErrorCode(err) ?? 'Failed to load net worth history.');
              store.setHistoryLoading(false);
              return EMPTY;
            })
          )
        )
      )
    ),
  };
}

interface HookStore extends EffectsStore {
  load: (range: HistoryRange) => void;
  loadNetWorthHistory: (range: HistoryRange) => void;
}

export function dashboardHooks(store: HookStore): void {
  const bankSyncService = inject(BankSyncService);

  // The `range` query param is synced to `historyRange` by `withUrlSync` in the store (restored
  // on a refresh / deep-link, written with replaceUrl so range clicks don't pollute browser
  // history). Reload BOTH the history chart and the range-scoped dashboard statistics whenever
  // it changes. The initial emission doubles as the first load, so there is no separate load().
  toObservable(store.historyRange)
    .pipe(
      tap(range => {
        untracked(() => {
          store.loadNetWorthHistory(range);
          store.load(range);
        });
      })
    )
    .subscribe();

  rxMethod<void>(
    pipe(
      switchMap(() =>
        timer(REFRESH_INTERVAL_MS, REFRESH_INTERVAL_MS).pipe(
          // The catch sits inside the inner stream: one failed refresh must not end the timer.
          switchMap(() =>
            bankSyncService
              .getDashboardData(
                DashboardRangeUtils.months(untracked(store.historyRange)),
                DashboardRangeUtils.windowMonths(untracked(store.historyRange)),
                DashboardRangeUtils.windowFrom(untracked(store.historyRange))
              )
              .pipe(
                tap(data =>
                  untracked(() => {
                    store.setData(data);
                    store.setSuccess();
                  })
                ),
                catchError((err: unknown) => {
                  untracked(() => store.setError(extractErrorCode(err)));
                  return EMPTY;
                })
              )
          )
        )
      )
    )
  )();
}
