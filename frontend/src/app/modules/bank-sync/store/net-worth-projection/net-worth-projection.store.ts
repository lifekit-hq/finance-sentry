import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {netWorthProjectionComputed} from './net-worth-projection.computed';
import {netWorthProjectionEffects, netWorthProjectionHooks} from './net-worth-projection.effects';
import {netWorthProjectionMethods} from './net-worth-projection.methods';
import {initialNetWorthProjectionState} from './net-worth-projection.state';

export const NetWorthProjectionStore = signalStore(
  withState(initialNetWorthProjectionState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load the net worth projection.'}),
  withMethods(netWorthProjectionMethods),
  withComputed(netWorthProjectionComputed),
  withMethods(netWorthProjectionEffects),
  withHooks({onInit: netWorthProjectionHooks})
);
