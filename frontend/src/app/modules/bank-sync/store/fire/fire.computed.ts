import {computed, type Signal} from '@angular/core';

import {type FireProjection} from '../../models/fire/fire.model';
import {FireTileUtils} from '../../utils/fire-tile.utils';

interface StateSignals {
  projection: Signal<Nullable<FireProjection>>;
  status: Signal<AsyncStatus>;
}

export function fireComputed(store: StateSignals) {
  // Below the complete-month minimum the backend answers InsufficientHistory and the tile does
  // not render at all — a date off one or two months is noise wearing a number's clothes. A
  // failed load hides it too: the tile is supplementary, so it never shouts over the dashboard.
  const visible = computed(() => {
    const projection = store.projection();
    return projection !== null && projection.status !== 'InsufficientHistory';
  });

  return {
    isLoading: computed(() => store.status() === 'loading'),
    visible,
    headline: computed(() => {
      const projection = store.projection();
      return projection ? FireTileUtils.headline(projection) : '';
    }),
    runway: computed(() => {
      const projection = store.projection();
      return projection ? FireTileUtils.runway(projection) : null;
    }),
    assumptions: computed(() => {
      const projection = store.projection();
      return projection ? FireTileUtils.assumptions(projection) : '';
    }),
    progressPercent: computed(() => {
      const projection = store.projection();
      return projection
        ? FireTileUtils.progressPercent(projection.currentNetWorth, projection.target)
        : 0;
    }),
    hasStaleSleeves: computed(() => store.projection()?.hasStaleSleeves ?? false),
  };
}
