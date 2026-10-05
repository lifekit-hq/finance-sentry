import {inject, type Signal} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, pipe, switchMap, tap} from 'rxjs';

import {type Alert} from '../../models/alert/alert.model';
import {type AlertsQuery} from '../../models/alert/alerts-query.model';
import {AlertsService} from '../../services/alerts.service';

interface EffectsStore {
  setData: (alerts: Alert[], totalCount: number, unreadCount: number) => void;
  setUnreadCount: (n: number) => void;
  setStatus: (status: AsyncStatus) => void;
  markReadLocal: (id: string) => void;
  markAllReadLocal: () => void;
  dismissLocal: (id: string) => void;
}

export function alertsEffects(store: EffectsStore) {
  const api = inject(AlertsService);

  return {
    load: rxMethod<AlertsQuery>(
      pipe(
        tap(() => store.setStatus('loading')),
        switchMap(({filter, page, pageSize}) =>
          api.getAlerts(filter, page, pageSize).pipe(
            tap(res => store.setData(res.items, res.totalCount, res.unreadCount)),
            catchError(() => {
              store.setStatus('error');
              return EMPTY;
            })
          )
        )
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
