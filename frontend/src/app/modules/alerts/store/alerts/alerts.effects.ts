import {inject, type Signal} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, debounceTime, EMPTY, exhaustMap, filter, pipe, switchMap, tap} from 'rxjs';

import {type Alert} from '../../models/alert/alert.model';
import {type AlertsQuery} from '../../models/alert/alerts-query.model';
import {AlertsService} from '../../services/alerts.service';

export const ALERT_SEARCH_DEBOUNCE_MS = 300;

interface EffectsStore {
  search: Signal<string>;
  setSearch: (search: string) => void;
  setData: (alerts: Alert[], totalCount: number, unreadCount: number) => void;
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

  return {
    load: rxMethod<AlertsQuery>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(({filter: kind, page, pageSize, search}) =>
          api.getAlerts(kind, page, pageSize, search).pipe(
            tap(res => {
              store.setData(res.items, res.totalCount, res.unreadCount);
              store.setSuccess();
            }),
            catchError(() => {
              store.setError(null);
              return EMPTY;
            })
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
