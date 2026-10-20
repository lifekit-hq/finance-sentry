import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {type HistoryRange} from '../../models/dashboard/dashboard.model';
import {type NetWorthBridgeResponse} from '../../models/net-worth-bridge/net-worth-bridge.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {DashboardStore} from '../dashboard/dashboard.store';

interface StoreMethods {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setBridge(bridge: NetWorthBridgeResponse): void;
}

export function netWorthBridgeEffects(store: StoreMethods) {
  const service = inject(BankSyncService);
  const dashboard = inject(DashboardStore);

  const load = rxMethod<HistoryRange>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(range =>
        service.getNetWorthBridge(range).pipe(
          tap(bridge => {
            store.setBridge(bridge);
            store.setSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setError(code),
          })
        )
      )
    )
  );

  return {
    load,
    retry(): void {
      load(dashboard.historyRange());
    },
  };
}

export function netWorthBridgeHooks(store: ReturnType<typeof netWorthBridgeEffects>) {
  const dashboard = inject(DashboardStore);
  return {
    onInit: () => {
      // Follows the hero's range chips: the bridge explains the change the hero shows.
      store.load(dashboard.historyRange);
    },
  };
}
