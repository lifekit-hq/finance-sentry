import {inject, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {
  type FlowBreakdown,
  type FlowBreakdownRequest,
} from '../../models/flow-breakdown/flow-breakdown.model';
import {BankSyncService} from '../../services/bank-sync.service';

interface EffectsStore {
  setLoading: () => void;
  setSuccess: () => void;
  setBreakdown: (breakdown: FlowBreakdown) => void;
  setError: (errorCode: Nullable<string>) => void;
}

export function flowBreakdownEffects(store: EffectsStore) {
  const bankSyncService = inject(BankSyncService);

  return {
    load: rxMethod<FlowBreakdownRequest>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(request =>
          (request.kind === 'month'
            ? bankSyncService.getFlowBreakdown(request.month)
            : bankSyncService.getFlowBreakdownRange(request.from, request.to, request.months)
          ).pipe(
            tap(breakdown => {
              store.setBreakdown(breakdown);
              store.setSuccess();
            }),
            StoreErrorUtils.catchAndSetError({
              setError: (code: Nullable<string>) => store.setError(code),
            })
          )
        )
      )
    ),
  };
}

interface HookStore {
  request: Signal<FlowBreakdownRequest>;
  load: (request: Signal<FlowBreakdownRequest>) => void;
}

/**
 * Loads whatever the query params (synced into the store by `withUrlSync`) ask for, again on every
 * change. rxMethod ties the subscription to the store's injector, so it tears down with the page.
 */
export function flowBreakdownHooks(store: HookStore): void {
  store.load(store.request);
}
