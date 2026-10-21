import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type Alert} from '../../models/alert/alert.model';
import {AlertsService} from '../../services/alerts.service';

interface EffectsStore {
  setTopAlert: (alert: Alert | undefined, unreadCount: number) => void;
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
}

export function topAlertEffects(store: EffectsStore) {
  const api = inject(AlertsService);
  return {
    /** The newest unread alert and how many are unread: one row of the unread filter. */
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(() =>
          api.getAlerts('unread', 1, 1).pipe(
            tap(res => {
              store.setTopAlert(res.items[0], res.totalCount);
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
  };
}

export function topAlertHooks(store: {load: () => unknown}): void {
  store.load();
}
