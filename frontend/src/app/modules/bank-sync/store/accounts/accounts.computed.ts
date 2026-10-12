import {computed, type Signal} from '@angular/core';
import {type DonutSegment} from '@lifekit-hq/ui';

import {
  CASH_COLOUR,
  CRYPTO_COLOUR,
  EQUITY_COLOUR,
  NEUTRAL_COLOUR,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {type ChartColour} from '../../../../shared/models/chart-colour/chart-colour.model';
import {
  type AccountCategory,
  type CategorySection,
  type NetWorthBreakdownRow,
  type WealthSummaryResponse,
} from '../../../../shared/models/wealth/wealth.model';
import {ChartColorUtils} from '../../../../shared/utils/chart-color.utils';

const PERCENT = 100;

interface StateSignals {
  summary: Signal<Nullable<WealthSummaryResponse>>;
  status: Signal<'idle' | 'loading' | 'success' | 'error'>;
}

const CATEGORY_LABEL: Record<AccountCategory, string> = {
  banking: 'Banking',
  brokerage: 'Brokerage',
  crypto: 'Crypto',
  other: 'Other',
};

// Each sleeve keeps the colour of the asset class it holds: banking is cash, brokerage is equity.
// Resolved when the donut is built, since a canvas cannot read a CSS var.
export const CATEGORY_COLOUR: Record<AccountCategory, ChartColour> = {
  banking: CASH_COLOUR,
  brokerage: EQUITY_COLOUR,
  crypto: CRYPTO_COLOUR,
  other: NEUTRAL_COLOUR,
};

const SECTION_ORDER: Omit<CategorySection, 'summary'>[] = [
  {category: 'banking', title: 'Banking', institutionNoun: 'institution', rowNoun: 'account'},
  {category: 'brokerage', title: 'Brokerage', institutionNoun: 'broker', rowNoun: 'position'},
  {category: 'crypto', title: 'Digital assets', institutionNoun: 'exchange', rowNoun: 'asset'},
];

export function accountsComputed(store: StateSignals) {
  return {
    isEmpty: computed(
      () => store.status() === 'success' && (store.summary()?.categories ?? []).length === 0
    ),
    totalNetWorth: computed(() => store.summary()?.totalNetWorth ?? 0),
    baseCurrency: computed(() => store.summary()?.baseCurrency ?? 'USD'),
    // The inventory sections, in display order. The noun is what one institution of the
    // section is called ("2 brokers"), and the child noun what its rows are ("positions").
    categorySections: computed((): CategorySection[] => {
      const categories = store.summary()?.categories ?? [];
      return SECTION_ORDER.flatMap(def => {
        const summary = categories.find(c => c.category === def.category);
        return summary ? [{...def, summary}] : [];
      });
    }),
    netWorthSegments: computed((): DonutSegment[] =>
      (store.summary()?.categories ?? [])
        .filter(cat => cat.totalInBaseCurrency > 0)
        .map(cat => ({
          label: CATEGORY_LABEL[cat.category],
          value: cat.totalInBaseCurrency,
          color: ChartColorUtils.canvas(CATEGORY_COLOUR[cat.category]),
        }))
    ),
    netWorthBreakdown: computed((): NetWorthBreakdownRow[] => {
      const positive = (store.summary()?.categories ?? []).filter(
        cat => cat.totalInBaseCurrency > 0
      );
      const total = positive.reduce((sum, cat) => sum + cat.totalInBaseCurrency, 0);
      return positive
        .map(cat => ({
          label: CATEGORY_LABEL[cat.category],
          color: ChartColorUtils.css(CATEGORY_COLOUR[cat.category]),
          value: cat.totalInBaseCurrency,
          institutionCount: cat.institutionCount,
          percent: total > 0 ? (cat.totalInBaseCurrency / total) * PERCENT : 0,
        }))
        .sort((a, b) => b.value - a.value);
    }),
  };
}
