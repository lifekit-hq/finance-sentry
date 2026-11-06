import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type ConnectIbkrFlexRequest} from '../../models/ibkr/ibkr.model';
import {IBKRService} from '../../services/ibkr.service';
import {type IbkrConnectState} from './ibkr-connect.state';

interface EffectsStore extends WritableStateSource<IbkrConnectState> {
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
}

function extractCode(err: unknown): Nullable<string> {
  const direct = (err as {errorCode?: string}).errorCode;
  return direct ?? extractErrorCode(err);
}

export function ibkrConnectEffects(store: EffectsStore) {
  const ibkr = inject(IBKRService);

  const validate = rxMethod<ConnectIbkrFlexRequest>(
    pipe(
      tap(() => {
        patchState(store, {preview: null});
        store.setLoading();
      }),
      switchMap(request =>
        ibkr.validateFlex(request).pipe(
          tap(preview => {
            patchState(store, {preview});
            store.setSuccess();
          }),
          catchError((err: unknown) => {
            store.setError(extractCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  return {validate};
}
