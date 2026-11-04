import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {InzhurService} from '../../services/inzhur.service';
import {type InzhurConnectState} from './inzhur-connect.state';

interface EffectsStore extends WritableStateSource<InzhurConnectState> {
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
}

export function inzhurConnectEffects(store: EffectsStore) {
  const inzhur = inject(InzhurService);

  const loadStatus = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        inzhur.getStatus().pipe(
          tap(connection => {
            patchState(store, {connection});
            store.setSuccess();
          }),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
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
