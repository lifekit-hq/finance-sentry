import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {concentrationHistoryComputed} from './concentration-history.computed';
import {
  concentrationHistoryEffects,
  concentrationHistoryHooks,
} from './concentration-history.effects';
import {concentrationHistoryMethods} from './concentration-history.methods';
import {initialConcentrationHistoryState} from './concentration-history.state';

/** Component-scoped: the risk picture's concentration-and-allocation chart. */
export const ConcentrationHistoryStore = signalStore(
  withState(initialConcentrationHistoryState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load the concentration history.'}),
  withMethods(concentrationHistoryMethods),
  withComputed(concentrationHistoryComputed),
  withMethods(concentrationHistoryEffects),
  withHooks(concentrationHistoryHooks)
);
