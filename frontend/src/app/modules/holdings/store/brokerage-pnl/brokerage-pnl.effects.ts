import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {type BrokeragePnl} from '../../models/brokerage-pnl/brokerage-pnl.model';
import {BrokeragePnlService} from '../../services/brokerage-pnl.service';

interface StoreMethods {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setPnl(pnl: BrokeragePnl): void;
}

export function brokeragePnlEffects(store: StoreMethods) {
  const service = inject(BrokeragePnlService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        service.getPnl().pipe(
          tap(pnl => {
            store.setPnl(pnl);
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

export function brokeragePnlHooks(store: ReturnType<typeof brokeragePnlEffects>) {
  return {
    onInit: () => {
      store.load();
    },
  };
}
