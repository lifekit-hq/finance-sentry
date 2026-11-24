import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type SleeveCosts} from '../../models/cost-of-investing/cost-of-investing.model';
import {type CostOfInvestingState} from './cost-of-investing.state';

export function costOfInvestingMethods(store: WritableStateSource<CostOfInvestingState>) {
  return {
    setCosts(costs: SleeveCosts): void {
      patchState(store, {costs});
    },
  };
}
