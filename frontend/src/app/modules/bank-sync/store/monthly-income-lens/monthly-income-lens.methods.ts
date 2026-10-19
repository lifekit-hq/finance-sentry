import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type MonthlyIncomeLens} from '../../models/income-lens/income-lens.model';
import {type MonthlyIncomeLensState} from './monthly-income-lens.state';

export function monthlyIncomeLensMethods(store: WritableStateSource<MonthlyIncomeLensState>) {
  return {
    setLens(lens: MonthlyIncomeLens): void {
      patchState(store, {lens});
    },
  };
}
