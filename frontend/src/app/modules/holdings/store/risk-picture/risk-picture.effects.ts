import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {type RiskPicture} from '../../models/risk-picture/risk-picture.model';
import {RiskPictureService} from '../../services/risk-picture.service';

interface StoreMethods {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setPicture(picture: RiskPicture): void;
}

export function riskPictureEffects(store: StoreMethods) {
  const service = inject(RiskPictureService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        service.getRiskPicture().pipe(
          tap(picture => {
            store.setPicture(picture);
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

export function riskPictureHooks(store: ReturnType<typeof riskPictureEffects>) {
  return {
    onInit: () => {
      store.load();
    },
  };
}
