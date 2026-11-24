import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {type BookIncome} from '../../models/book-income/book-income.model';
import {BookIncomeService} from '../../services/book-income.service';

interface StoreMethods {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setIncome(income: BookIncome): void;
}

export function bookIncomeEffects(store: StoreMethods) {
  const service = inject(BookIncomeService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        service.getIncome().pipe(
          tap(income => {
            store.setIncome(income);
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

export function bookIncomeHooks(store: ReturnType<typeof bookIncomeEffects>) {
  return {
    onInit: () => {
      store.load();
    },
  };
}
