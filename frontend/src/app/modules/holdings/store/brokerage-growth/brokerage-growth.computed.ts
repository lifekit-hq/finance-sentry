import {computed, type Signal} from '@angular/core';

import {ProviderUtils} from '../../../../shared/utils/provider.utils';
import {
  IBKR_PROVIDER,
  MWR_PERCENT_DIGITS,
} from '../../constants/brokerage-growth/brokerage-growth.constants';
import {
  type BrokerageGrowth,
  type BrokerageGrowthView,
} from '../../models/brokerage-growth/brokerage-growth.model';

interface StateSignals {
  growth: Signal<Nullable<BrokerageGrowth>>;
}

export function brokerageGrowthComputed(store: StateSignals) {
  const missing = computed(() => store.growth()?.sourcesWithoutFlows ?? []);
  return {
    // A user with no brokerage value history (crypto only, or nothing linked yet) sees no section.
    hasHistory: computed(() => store.growth()?.seriesStart != null),
    view: computed<BrokerageGrowthView>(() => {
      if (missing().length > 0) {
        return 'no-flows';
      }
      return store.growth()?.growth == null ? 'short-history' : 'split';
    }),
    missingProviderLabels: computed(() =>
      missing()
        .map(slug => ProviderUtils.label(slug))
        .join(' and ')
    ),
    isIbkrMissing: computed(() => missing().includes(IBKR_PROVIDER)),
    /** The headline's Modified Dietz percent, signed; null whenever the split is withheld. */
    returnLabel: computed(() => {
      const percent = store.growth()?.headline.percent;
      if (percent == null) {
        return null;
      }
      return `${percent > 0 ? '+' : ''}${percent.toFixed(MWR_PERCENT_DIGITS)}%`;
    }),
    showsNewlyLinked: computed(() => {
      const linked = store.growth()?.newlyLinked;
      return linked != null && linked !== 0;
    }),
  };
}
