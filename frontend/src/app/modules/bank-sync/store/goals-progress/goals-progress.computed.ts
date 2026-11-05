import {computed, inject, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {type GoalsProgress} from '../../models/goals-progress/goals-progress.model';
import {GoalsProgressUtils} from '../../utils/goals-progress.utils';

interface StateSignals {
  progress: Signal<Nullable<GoalsProgress>>;
  isLoading: Signal<boolean>;
  errorMessage: Signal<string>;
}

export function goalsProgressComputed(store: StateSignals) {
  const connectivity = inject(ConnectivityService);
  const hasData = computed(() => store.progress() !== null);

  return {
    hasData,
    // Reloads in place: a retry keeps the goals; offline only counts while nothing is on screen.
    view: computed(() =>
      AsyncViewUtils.resolve({
        isLoading: store.isLoading(),
        hasData: hasData(),
        errorMessage: store.errorMessage(),
        offline: connectivity.offline() && !hasData(),
        lastSyncedAt: null,
      })
    ),
    // No goals on the IPS: the card renders its empty state, which says how to add one.
    isEmpty: computed(() => store.progress()?.status === 'NoGoals'),
    rows: computed(() => {
      const progress = store.progress();
      return progress?.status === 'Shown' ? GoalsProgressUtils.rows(progress) : [];
    }),
    plan: computed(() => {
      const progress = store.progress();
      return progress?.status === 'Shown' ? GoalsProgressUtils.planLine(progress) : null;
    }),
    assumptions: computed(() => {
      const progress = store.progress();
      return progress?.status === 'Shown' ? GoalsProgressUtils.assumptions(progress) : '';
    }),
    hasStaleSleeves: computed(() => store.progress()?.hasStaleSleeves ?? false),
  };
}
