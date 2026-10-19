import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type NetWorthBridgeResponse} from '../../models/net-worth-bridge/net-worth-bridge.model';
import {type NetWorthBridgeState} from './net-worth-bridge.state';

export function netWorthBridgeMethods(store: WritableStateSource<NetWorthBridgeState>) {
  return {
    setBridge(bridge: NetWorthBridgeResponse): void {
      patchState(store, {bridge});
    },
  };
}
