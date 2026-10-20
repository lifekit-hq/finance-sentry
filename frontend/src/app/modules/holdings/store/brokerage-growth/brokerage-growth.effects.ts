import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {
  type BrokerageGrowth,
  type GrowthSleeve,
} from '../../models/brokerage-growth/brokerage-growth.model';
import {BrokerageGrowthService} from '../../services/brokerage-growth.service';

interface StoreMethods {
  sleeve: GrowthSleeve;
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setGrowth(growth: BrokerageGrowth): void;
}

export function brokerageGrowthEffects(store: StoreMethods) {
  const service = inject(BrokerageGrowthService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        service.getGrowth(store.sleeve).pipe(
          tap(growth => {
            store.setGrowth(growth);
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

export function brokerageGrowthHooks(store: ReturnType<typeof brokerageGrowthEffects>) {
  return {
    onInit: () => {
      store.load();
    },
  };
}
