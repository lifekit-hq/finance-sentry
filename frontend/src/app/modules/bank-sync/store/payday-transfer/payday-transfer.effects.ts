import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable, ToastService} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, pipe, switchMap, tap} from 'rxjs';

import {
  type PaydayTransfer,
  type PaydayTransferSettings,
} from '../../models/payday-transfer/payday-transfer.model';
import {PaydayTransferService} from '../../services/payday-transfer.service';

interface EffectsStore {
  setLoading: () => void;
  setSuccess: () => void;
  setError: (errorCode: Nullable<string>) => void;
  setSaveLoading: () => void;
  setSaveSuccess: () => void;
  setSaveError: (errorCode: Nullable<string>) => void;
  setTransfer: (transfer: PaydayTransfer) => void;
  setEditing: (editing: boolean) => void;
}

export function paydayTransferEffects(store: EffectsStore) {
  const service = inject(PaydayTransferService);
  const toast = inject(ToastService);

  const fetchTransfer = () =>
    service.getPaydayTransfer().pipe(
      tap(transfer => {
        store.setTransfer(transfer);
        store.setSuccess();
      }),
      catchError((err: unknown) => {
        store.setError(extractErrorCode(err));
        return EMPTY;
      })
    );

  return {
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(() => fetchTransfer())
      )
    ),
    // The figure is recomputed server-side from the saved settings, so a save is followed by a fresh read.
    saveSettings: rxMethod<PaydayTransferSettings>(
      pipe(
        tap(() => store.setSaveLoading()),
        exhaustMap(settings =>
          service.updateSettings(settings).pipe(
            tap(() => {
              store.setSaveSuccess();
              store.setEditing(false);
              toast.show('Payday transfer settings saved', 'success');
            }),
            switchMap(() => fetchTransfer()),
            catchError((err: unknown) => {
              store.setSaveError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
  };
}

interface HookStore extends EffectsStore {
  load: () => void;
}

export function paydayTransferHooks(store: HookStore): void {
  store.load();
}
