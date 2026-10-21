import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {type DateRange} from '../../../../shared/utils/date-range.utils';
import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {type NetWorthBridgeResponse} from '../../models/net-worth-bridge/net-worth-bridge.model';
import {NET_WORTH_BRIDGE_WINDOW} from '../../providers/net-worth-bridge-window.provider';
import {BankSyncService} from '../../services/bank-sync.service';

interface StoreMethods {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setBridge(bridge: NetWorthBridgeResponse): void;
}

export function netWorthBridgeEffects(store: StoreMethods) {
  const service = inject(BankSyncService);
  const bridgeWindow = inject(NET_WORTH_BRIDGE_WINDOW);

  const load = rxMethod<DateRange>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(days =>
        service.getNetWorthBridge(days).pipe(
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
      load(bridgeWindow());
    },
  };
}

export function netWorthBridgeHooks(store: ReturnType<typeof netWorthBridgeEffects>) {
  const bridgeWindow = inject(NET_WORTH_BRIDGE_WINDOW);
  return {
    onInit: () => {
      // Follows the page's window: on Home the range chips, so it explains the change the hero shows.
      store.load(bridgeWindow);
    },
  };
}
