import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {NET_WORTH_BRIDGE_DEFAULT_ERROR} from '../../constants/net-worth-bridge/net-worth-bridge.constants';
import {netWorthBridgeComputed} from './net-worth-bridge.computed';
import {netWorthBridgeEffects, netWorthBridgeHooks} from './net-worth-bridge.effects';
import {netWorthBridgeMethods} from './net-worth-bridge.methods';
import {initialNetWorthBridgeState} from './net-worth-bridge.state';

/** Component-scoped: the net worth bridge over the window its page provides (`NET_WORTH_BRIDGE_WINDOW`). */
export const NetWorthBridgeStore = signalStore(
  withState(initialNetWorthBridgeState),
  withAsyncStatus({defaultErrorMessage: NET_WORTH_BRIDGE_DEFAULT_ERROR}),
  withMethods(netWorthBridgeMethods),
  withComputed(netWorthBridgeComputed),
  withMethods(netWorthBridgeEffects),
  withHooks(netWorthBridgeHooks)
);
