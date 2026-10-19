import {HostAttributeToken, inject} from '@angular/core';
import {withAsyncStatus} from '@lifekit-hq/core';
import {
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withProps,
  withState,
} from '@ngrx/signals';

import {
  DEFAULT_GROWTH_SLEEVE,
  GROWTH_SLEEVE_COPY,
} from '../../constants/brokerage-growth/brokerage-growth.constants';
import {type GrowthSleeve} from '../../models/brokerage-growth/brokerage-growth.model';
import {brokerageGrowthComputed} from './brokerage-growth.computed';
import {brokerageGrowthEffects, brokerageGrowthHooks} from './brokerage-growth.effects';
import {brokerageGrowthMethods} from './brokerage-growth.methods';
import {initialBrokerageGrowthState} from './brokerage-growth.state';

/**
 * Component-scoped: the holdings card's contributions-vs-growth section. The host's static `sleeve`
 * attribute picks the sleeve (`<fns-brokerage-growth sleeve="crypto" />`); none means the brokerage.
 */
export const BrokerageGrowthStore = signalStore(
  withState(initialBrokerageGrowthState),
  withProps(() => {
    const sleeve: GrowthSleeve =
      inject(new HostAttributeToken('sleeve'), {optional: true}) === 'crypto'
        ? 'crypto'
        : DEFAULT_GROWTH_SLEEVE;
    return {sleeve, copy: GROWTH_SLEEVE_COPY[sleeve]};
  }),
  withAsyncStatus({defaultErrorMessage: 'Failed to load contributions and growth.'}),
  withMethods(brokerageGrowthMethods),
  withComputed(brokerageGrowthComputed),
  withMethods(brokerageGrowthEffects),
  withHooks(brokerageGrowthHooks)
);
