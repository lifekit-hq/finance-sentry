import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type FxImpact} from '../../models/fx-impact/fx-impact.model';
import {type FxImpactState} from './fx-impact.state';

export function fxImpactMethods(store: WritableStateSource<FxImpactState>) {
  return {
    setImpact(impact: FxImpact): void {
      patchState(store, {
        baseCurrency: impact.baseCurrency,
        plans: impact.plans,
        totals: impact.totals,
      });
    },
    clearImpact(): void {
      patchState(store, {plans: [], totals: null});
    },
    setEnabled(enabled: boolean): void {
      patchState(store, {enabled});
    },
  };
}
