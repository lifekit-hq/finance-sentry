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
import {type GrowthSleeve} from '@shared/models/brokerage-growth/brokerage-growth.model';

import {costOfInvestingComputed} from './cost-of-investing.computed';
import {costOfInvestingEffects, costOfInvestingHooks} from './cost-of-investing.effects';
import {costOfInvestingMethods} from './cost-of-investing.methods';
import {initialCostOfInvestingState} from './cost-of-investing.state';

/**
 * Component-scoped: the growth card's cost-of-investing row. The host's static `sleeve` attribute picks the
 * sleeve (`<fns-cost-of-investing sleeve="crypto" />`); none means the brokerage.
 */
export const CostOfInvestingStore = signalStore(
  withState(initialCostOfInvestingState),
  withProps(() => {
    const sleeve: GrowthSleeve =
      inject(new HostAttributeToken('sleeve'), {optional: true}) === 'crypto'
        ? 'crypto'
        : 'brokerage';
    return {sleeve};
  }),
  withAsyncStatus({defaultErrorMessage: 'Failed to load the cost of investing.'}),
  withMethods(costOfInvestingMethods),
  withComputed(costOfInvestingComputed),
  withMethods(costOfInvestingEffects),
  withHooks(costOfInvestingHooks)
);
