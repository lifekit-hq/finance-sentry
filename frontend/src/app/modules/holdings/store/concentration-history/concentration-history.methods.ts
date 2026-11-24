import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type ConcentrationHistory,
  type ConcentrationMeasure,
} from '../../models/risk-picture/risk-picture.model';
import {type ConcentrationHistoryState} from './concentration-history.state';

export function concentrationHistoryMethods(store: WritableStateSource<ConcentrationHistoryState>) {
  return {
    setHistory(history: ConcentrationHistory): void {
      patchState(store, {history});
    },
    selectMeasure(measure: ConcentrationMeasure): void {
      patchState(store, {measure});
    },
  };
}
