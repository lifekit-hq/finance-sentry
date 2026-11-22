import {inject, type Signal} from '@angular/core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {type Alert, type AlertFilter} from '@shared/models/alert/alert.model';
import {
  catchError,
  debounceTime,
  EMPTY,
  exhaustMap,
  filter,
  pipe,
  Subject,
  switchMap,
  takeUntil,
  tap,
} from 'rxjs';

import {type AlertsQuery} from '../../models/alert/alerts-query.model';
import {AlertsService} from '../../services/alerts.service';
import {ALERTS_PAGE_SIZE} from './alerts.state';

export const ALERT_SEARCH_DEBOUNCE_MS = 300;

interface EffectsStore {
  query: Signal<AlertsQuery>;
  search: Signal<string>;
  filter: Signal<AlertFilter>;
  status: Signal<AsyncStatus>;
  alerts: Signal<Alert[]>;
  setSearch: (search: string) => void;
  setData: (alerts: Alert[], totalCount: number, unreadCount: number) => void;
  appendData: (alerts: Alert[], totalCount: number, unreadCount: number) => void;
  clearData: () => void;
  setAppendLoading: () => void;
  setAppendSuccess: () => void;
  setAppendError: (errorCode: Nullable<string>) => void;
  setAppendIdle: () => void;
  setUnreadCount: (n: number) => void;
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
  markReadLocal: (id: string) => void;
  markAllReadLocal: () => void;
  dismissLocal: (id: string) => void;
}

export function alertsEffects(store: EffectsStore) {
  const api = inject(AlertsService);
  const firstPageStarted$ = new Subject<void>();
  let rowsQuery: Nullable<AlertsQuery> = null;

  /** Loads the first page of a query; the rows on screen stay (dimmed) until it lands; a failure drops them only when they belong to another query. */
  const load = rxMethod<AlertsQuery>(
    pipe(
      tap(() => {
        firstPageStarted$.next();
        store.setAppendIdle();
        store.setLoading();
      }),
      switchMap(query =>
        api.getAlerts(query.filter, 1, ALERTS_PAGE_SIZE, query.search).pipe(
          tap(res => {
            rowsQuery = query;
            store.setData(res.items, res.totalCount, res.unreadCount);
            store.setAppendSuccess();
            store.setSuccess();
          }),
          catchError(() => {
            if (rowsQuery?.filter !== query.filter || rowsQuery.search !== query.search) {
              rowsQuery = null;
              store.clearData();
            }
            store.setError(null);
            return EMPTY;
          })
        )
      )
    )
  );

  return {
    load,
    /** Loads the query on screen again (pull to refresh); the rows stay until the first page lands. */
    reload: () => load(store.query()),
    /**
     * Appends the next page (the infinite list's paging contract): asked near the end and on retry.
     * The page follows the rows on screen, so a row removed locally is refilled, not skipped.
     */
    loadMore: rxMethod<void>(
      pipe(
        filter(() => store.status() !== 'loading'),
        tap(() => store.setAppendLoading()),
        exhaustMap(() =>
          api
            .getAlerts(
              store.filter(),
              Math.floor(store.alerts().length / ALERTS_PAGE_SIZE) + 1,
              ALERTS_PAGE_SIZE,
              store.search().trim()
            )
            .pipe(
              tap(res => {
                store.appendData(res.items, res.totalCount, res.unreadCount);
                store.setAppendSuccess();
              }),
              catchError(() => {
                store.setAppendError(null);
                return EMPTY;
              }),
              takeUntil(firstPageStarted$)
            )
        )
      )
    ),
    /** Follows the search box: debounced, then written to the state the list query reads. */
    applySearch: rxMethod<string>(
      pipe(
        debounceTime(ALERT_SEARCH_DEBOUNCE_MS),
        filter(search => search.trim() !== store.search().trim()),
        tap(search => store.setSearch(search))
      )
    ),
    loadUnreadCount: rxMethod<void>(
      pipe(
        switchMap(() =>
          api.getUnreadCount().pipe(
            tap(res => store.setUnreadCount(res.count)),
            catchError(() => EMPTY)
          )
        )
      )
    ),
    markRead: rxMethod<string>(
      pipe(
        exhaustMap(id =>
          api.markRead(id).pipe(
            tap(() => store.markReadLocal(id)),
            catchError(() => EMPTY)
          )
        )
      )
    ),
    markAllRead: rxMethod<void>(
      pipe(
        exhaustMap(() =>
          api.markAllRead().pipe(
            tap(() => store.markAllReadLocal()),
            catchError(() => EMPTY)
          )
        )
      )
    ),
    dismiss: rxMethod<string>(
      pipe(
        exhaustMap(id =>
          api.dismiss(id).pipe(
            tap(() => store.dismissLocal(id)),
            catchError(() => EMPTY)
          )
        )
      )
    ),
  };
}

interface HookStore {
  query: Signal<AlertsQuery>;
  load: (query: Signal<AlertsQuery>) => unknown;
  loadUnreadCount: () => void;
}

export function alertsHooks(store: HookStore): void {
  store.load(store.query);
  store.loadUnreadCount();
}
