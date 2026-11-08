import {computed, inject, type Signal} from '@angular/core';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {type DecisionsState} from './decisions.state';

interface StateSignals {
  pending: Signal<DecisionsState['pending']>;
  decided: Signal<DecisionsState['decided']>;
  isLoading: Signal<boolean>;
  errorMessage: Signal<string>;
  selectedId: Signal<DecisionsState['selectedId']>;
}

export function decisionsComputed(store: StateSignals) {
  const connectivity = inject(ConnectivityService);
  const hasData = computed(() => store.pending().length > 0 || store.decided().length > 0);
  return {
    hasData,
    /** The open detail's proposal, pending or decided; null while loading or when no such id. */
    selected: computed(() => {
      const id = store.selectedId();
      return [...store.pending(), ...store.decided()].find(rec => rec.id === id) ?? null;
    }),
    // Reloads in place: rows stay under the banner; offline only counts while nothing is on screen.
    view: computed(() =>
      AsyncViewUtils.resolve({
        isLoading: store.isLoading(),
        hasData: hasData(),
        errorMessage: store.errorMessage(),
        offline: connectivity.offline() && !hasData(),
        lastSyncedAt: null,
      })
    ),
  };
}
