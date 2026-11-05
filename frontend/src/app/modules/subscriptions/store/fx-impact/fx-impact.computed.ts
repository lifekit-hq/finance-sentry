import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {
  type FxImpactPlan,
  type FxImpactPlanView,
  type FxImpactTotals,
} from '../../models/fx-impact/fx-impact.model';
import {FxImpactUtils} from '../../utils/fx-impact.utils';

interface StateSignals {
  baseCurrency: Signal<string>;
  plans: Signal<FxImpactPlan[]>;
  totals: Signal<Nullable<FxImpactTotals>>;
  enabled: Signal<boolean>;
}

export function fxImpactComputed(store: StateSignals) {
  // The data gate: at least one active plan in a currency that is not USD, EUR or the base currency, with a
  // published rate history. A failed load leaves no plans, so the widget and its toggle stay hidden - it is
  // supplementary and never shouts over the page.
  const available = computed(() => store.plans().length > 0);

  return {
    available,
    showInsight: computed(() => available() && store.enabled()),
    planViews: computed((): FxImpactPlanView[] => store.plans().map(FxImpactUtils.planView)),
    summary: computed((): Nullable<string> => {
      const totals = store.totals();
      return totals === null ? null : FxImpactUtils.summaryLine(totals, store.baseCurrency());
    }),
    hasUnknownRemainder: computed(() => store.totals()?.hasUnknownRemainder === true),
  };
}
