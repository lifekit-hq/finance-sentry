import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {type ConcentrationHistory} from '../../models/risk-picture/risk-picture.model';
import {RiskPictureService} from '../../services/risk-picture.service';

interface StoreMethods {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setHistory(history: ConcentrationHistory): void;
}

export function concentrationHistoryEffects(store: StoreMethods) {
  const service = inject(RiskPictureService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        service.getConcentrationHistory().pipe(
          tap(history => {
            store.setHistory(history);
            store.setSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setError(code),
          })
        )
      )
    )
  );

  return {load};
}

export function concentrationHistoryHooks(store: ReturnType<typeof concentrationHistoryEffects>) {
  return {
    onInit: () => {
      store.load();
    },
  };
}
