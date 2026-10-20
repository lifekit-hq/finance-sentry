import {computed, type Signal} from '@angular/core';

import {type MonthlyIncomeLens} from '../../models/income-lens/income-lens.model';
import {IncomeLensUtils} from '../../utils/income-lens.utils';

interface StateSignals {
  lens: Signal<Nullable<MonthlyIncomeLens>>;
}

export function monthlyIncomeLensComputed(store: StateSignals) {
  // Before the first snapshot, or while net worth is zero or negative, the backend answers
  // NoNetWorth and the line renders nothing. A failed load hides it too: the lens is
  // supplementary to the hero figure.
  const visible = computed(() => store.lens()?.status === 'Shown');

  return {
    visible,
    monthlyIncome: computed(() => {
      const lens = store.lens();
      return lens ? IncomeLensUtils.monthlyIncome(lens) : '';
    }),
    assumptions: computed(() => {
      const lens = store.lens();
      return lens ? IncomeLensUtils.assumptions(lens) : '';
    }),
    hasStaleSleeves: computed(() => (store.lens()?.hasStaleSleeves ?? false) && visible()),
  };
}
