import {type Nullable} from '@lifekit-hq/ui';

import {type FxImpactPlan, type FxImpactTotals} from '../../models/fx-impact/fx-impact.model';

export interface FxImpactState {
  baseCurrency: string;
  plans: FxImpactPlan[];
  totals: Nullable<FxImpactTotals>;
  /** This device's choice to show the insight; it only matters while `plans` is non-empty. */
  enabled: boolean;
}

export const initialFxImpactState: FxImpactState = {
  baseCurrency: 'USD',
  plans: [],
  totals: null,
  enabled: true,
};
