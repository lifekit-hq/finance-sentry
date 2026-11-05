import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {brokeragePnlComputed} from './brokerage-pnl.computed';
import {brokeragePnlEffects, brokeragePnlHooks} from './brokerage-pnl.effects';
import {brokeragePnlMethods} from './brokerage-pnl.methods';
import {initialBrokeragePnlState} from './brokerage-pnl.state';

/** Component-scoped: the brokerage performance panel's capital in the window, realized and unrealized rows. */
export const BrokeragePnlStore = signalStore(
  withState(initialBrokeragePnlState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load realized and unrealized P&L.'}),
  withMethods(brokeragePnlMethods),
  withComputed(brokeragePnlComputed),
  withMethods(brokeragePnlEffects),
  withHooks(brokeragePnlHooks)
);
