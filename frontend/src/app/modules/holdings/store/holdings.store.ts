import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {holdingsComputed} from './holdings.computed';
import {holdingsEffects, holdingsHooks} from './holdings.effects';
import {holdingsMethods} from './holdings.methods';
import {initialHoldingsState} from './holdings.state';

export const HoldingsStore = signalStore(
  withState(initialHoldingsState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load positions.'}),
  withMethods(holdingsMethods),
  withComputed(holdingsComputed),
  withMethods(holdingsEffects),
  withHooks(holdingsHooks)
);
