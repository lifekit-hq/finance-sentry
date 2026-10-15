import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {brokerageGrowthComputed} from './brokerage-growth.computed';
import {brokerageGrowthEffects, brokerageGrowthHooks} from './brokerage-growth.effects';
import {brokerageGrowthMethods} from './brokerage-growth.methods';
import {initialBrokerageGrowthState} from './brokerage-growth.state';

/** Component-scoped: the holdings card's contributions-vs-growth section. */
export const BrokerageGrowthStore = signalStore(
  withState(initialBrokerageGrowthState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load contributions and growth.'}),
  withMethods(brokerageGrowthMethods),
  withComputed(brokerageGrowthComputed),
  withMethods(brokerageGrowthEffects),
  withHooks(brokerageGrowthHooks)
);
