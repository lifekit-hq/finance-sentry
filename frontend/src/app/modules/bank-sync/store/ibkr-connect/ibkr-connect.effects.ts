import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {patchState, type WritableStateSource} from '@ngrx/signals';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type ConnectIbkrFlexRequest} from '../../models/ibkr/ibkr.model';
import {IBKRService} from '../../services/ibkr.service';
import {type IbkrConnectState} from './ibkr-connect.state';

function extractCode(err: unknown): Nullable<string> {
  const direct = (err as {errorCode?: string}).errorCode;
  return direct ?? extractErrorCode(err);
}

export function ibkrConnectEffects(store: WritableStateSource<IbkrConnectState>) {
  const ibkr = inject(IBKRService);

  const validate = rxMethod<ConnectIbkrFlexRequest>(
    pipe(
      tap(() =>
        patchState(store, {validationStatus: 'validating', preview: null, errorCode: null})
      ),
      switchMap(request =>
        ibkr.validateFlex(request).pipe(
          tap(preview => patchState(store, {validationStatus: 'validated', preview})),
          catchError((err: unknown) => {
            patchState(store, {validationStatus: 'error', errorCode: extractCode(err)});
            return EMPTY;
          })
        )
      )
    )
  );

  return {validate};
}
