import {inject} from '@angular/core';
import {patchState, type WritableStateSource} from '@ngrx/signals';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {InzhurService} from '../../services/inzhur.service';
import {type InzhurConnectState} from './inzhur-connect.state';

export function inzhurConnectEffects(store: WritableStateSource<InzhurConnectState>) {
  const inzhur = inject(InzhurService);

  const loadStatus = rxMethod<void>(
    pipe(
      tap(() => patchState(store, {statusLoad: 'loading'})),
      switchMap(() =>
        inzhur.getStatus().pipe(
          tap(connection => patchState(store, {connection, statusLoad: 'loaded'})),
          catchError(() => {
            patchState(store, {statusLoad: 'error'});
            return EMPTY;
          })
        )
      )
    )
  );

  return {loadStatus};
}

export function inzhurConnectHooks(store: {loadStatus: () => void}): void {
  store.loadStatus();
}
