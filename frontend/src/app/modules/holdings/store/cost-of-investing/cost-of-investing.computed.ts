import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {type SleeveCosts} from '../../models/cost-of-investing/cost-of-investing.model';

const PERCENT = 100;

interface StoreState {
  costs: Signal<Nullable<SleeveCosts>>;
}

export function costOfInvestingComputed(store: StoreState) {
  return {
    /** The row shows only once something was paid. */
    hasCosts: computed(() => {
      const costs = store.costs();
      return costs !== null && costs.total !== 0;
    }),
    /** The nonzero parts in a fixed order, e.g. "Commissions 12.00 · Withholding tax 4.00". */
    parts: computed(() => {
      const costs = store.costs();
      if (costs === null) {
        return [];
      }
      return [
        {key: 'commissions', label: 'Commissions', amount: costs.commissions},
        {key: 'fees', label: 'Fees', amount: costs.fees},
        {key: 'fx', label: 'FX conversion', amount: costs.fxConversion},
        {key: 'withholding', label: 'Withholding tax', amount: costs.withholding},
      ].filter(part => part.amount !== 0);
    }),
    /** The share of returns as a one-decimal percent, e.g. "8.5%"; null when it cannot be measured. */
    shareLabel: computed(() => {
      const share = store.costs()?.shareOfReturns;
      return share === null || share === undefined ? null : `${(share * PERCENT).toFixed(1)}%`;
    }),
  };
}
