import {inject} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {type BookPerformance} from '../../models/book-performance/book-performance.model';
import {BookPerformanceService} from '../../services/book-performance.service';

interface StoreMethods {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setPerformance(performance: BookPerformance): void;
}

export function bookPerformanceEffects(store: StoreMethods) {
  const service = inject(BookPerformanceService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        service.getPerformance().pipe(
          tap(performance => {
            store.setPerformance(performance);
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

export function bookPerformanceHooks(store: ReturnType<typeof bookPerformanceEffects>) {
  return {
    onInit: () => {
      store.load();
    },
  };
}
