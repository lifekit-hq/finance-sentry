import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {PercentUtils} from '../../../../shared/utils/percent.utils';
import {ProviderUtils} from '../../../../shared/utils/provider.utils';
import {
  IBKR_PROVIDER,
  PERCENT_POINTS,
  RETURN_PERCENT_DIGITS,
} from '../../constants/brokerage-growth/brokerage-growth.constants';
import {
  type BrokerageGrowth,
  type BrokerageGrowthView,
  type GrowthTimingGap,
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
    /** The headline's Modified Dietz percent, for the stat's direction and colour; null whenever the split is withheld. */
    returnPercent: computed(() => store.growth()?.headline.percent ?? null),
    /** The headline's Modified Dietz percent, signed; null whenever the split is withheld. */
    returnLabel: computed(() => {
      const percent = store.growth()?.headline.percent;
      if (percent == null) {
        return null;
      }
      return PercentUtils.signed(percent, RETURN_PERCENT_DIGITS);
    }),
    /** P8: the time-weighted return against the money-weighted one; null unless both are measured. */
    timingGap: computed<Nullable<GrowthTimingGap>>(() => {
      const twr = store.growth()?.timeWeightedReturn;
      const mwr = store.growth()?.moneyWeightedReturn;
      if (twr == null || mwr == null) {
        return null;
      }
      return {
        investments: PercentUtils.signed(twr * PERCENT_POINTS, RETURN_PERCENT_DIGITS),
        money: PercentUtils.signed(mwr * PERCENT_POINTS, RETURN_PERCENT_DIGITS),
        points: (Math.abs(twr - mwr) * PERCENT_POINTS).toFixed(RETURN_PERCENT_DIGITS),
      };
    }),
    showsNewlyLinked: computed(() => {
      const linked = store.growth()?.newlyLinked;
      return linked != null && linked !== 0;
    }),
  };
}
