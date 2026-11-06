import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {BankSyncService} from '../../services/bank-sync.service';
import {type TrueLayerPickerState} from './truelayer-picker.state';

interface EffectsStore extends WritableStateSource<TrueLayerPickerState> {
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
}

export function trueLayerPickerEffects(store: EffectsStore) {
  const bankSync = inject(BankSyncService);

  /** Loads the bank list for a country; a new country cancels the in-flight request. */
  const loadProviders = rxMethod<string>(
    pipe(
      tap(() => {
        patchState(store, {providers: []});
        store.setLoading();
      }),
      switchMap(country =>
        bankSync.listTrueLayerProviders(country).pipe(
          tap(providers => {
            patchState(store, {providers});
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

  return {loadProviders};
}
