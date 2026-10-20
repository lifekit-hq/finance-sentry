import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {type NetWorthProjection} from '../../models/projection/projection.model';
import {ProjectionUtils} from '../../utils/projection.utils';

interface StateSignals {
  projection: Signal<Nullable<NetWorthProjection>>;
}

export function netWorthProjectionComputed(store: StateSignals) {
  // Below three complete months (or before the first snapshot) the backend answers
  // InsufficientHistory and the line renders nothing rather than a guess. A failed load hides it
  // too: the line is supplementary to the hero figure.
  const visible = computed(() => store.projection()?.status === 'Projected');

  return {
    visible,
    central: computed(() => {
      const projection = store.projection();
      return projection ? ProjectionUtils.netWorthCentral(projection) : '';
    }),
    range: computed(() => {
      const projection = store.projection();
      return projection ? ProjectionUtils.netWorthRange(projection) : '';
    }),
    assumptions: computed(() => {
      const projection = store.projection();
      return projection ? ProjectionUtils.netWorthAssumptions(projection) : '';
    }),
    hasStaleSleeves: computed(() => (store.projection()?.hasStaleSleeves ?? false) && visible()),
    horizonMonths: computed(() => store.projection()?.horizonMonths ?? 0),
  };
}
