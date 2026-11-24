import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {type GrowthSleeve} from '@shared/models/brokerage-growth/brokerage-growth.model';
import {pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {type SleeveCosts} from '../../models/cost-of-investing/cost-of-investing.model';
import {CostOfInvestingService} from '../../services/cost-of-investing.service';

interface StoreMethods {
  sleeve: GrowthSleeve;
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setCosts(costs: SleeveCosts): void;
}

export function costOfInvestingEffects(store: StoreMethods) {
  const service = inject(CostOfInvestingService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        service.getCosts(store.sleeve).pipe(
          tap(costs => {
            store.setCosts(costs);
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

export function costOfInvestingHooks(store: ReturnType<typeof costOfInvestingEffects>) {
  return {
    onInit: () => {
      store.load();
    },
  };
}
