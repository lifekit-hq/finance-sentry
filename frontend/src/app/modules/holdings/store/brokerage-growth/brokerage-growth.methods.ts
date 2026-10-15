import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type BrokerageGrowth} from '../../models/brokerage-growth/brokerage-growth.model';
import {type BrokerageGrowthState} from './brokerage-growth.state';

export function brokerageGrowthMethods(store: WritableStateSource<BrokerageGrowthState>) {
  return {
    setGrowth(growth: BrokerageGrowth): void {
      patchState(store, {growth});
    },
  };
}
