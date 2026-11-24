import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {
  type ConcentrationHistory,
  type ConcentrationMeasure,
  type ConcentrationView,
} from '../../models/risk-picture/risk-picture.model';
import {ConcentrationHistoryUtils} from '../../utils/concentration-history.utils';

interface StateSignals {
  history: Signal<Nullable<ConcentrationHistory>>;
  measure: Signal<ConcentrationMeasure>;
}

export function concentrationHistoryComputed(store: StateSignals) {
  return {
    view: computed<Nullable<ConcentrationView>>(() => {
      const history = store.history();
      return history ? ConcentrationHistoryUtils.view(history, store.measure()) : null;
    }),
    tooLittleHistory: computed<boolean>(() => !!store.history()?.reason),
  };
}
