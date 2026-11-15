import {computed, inject, type Signal} from '@angular/core';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {type ThesisRevisionsState} from './thesis-revisions.state';

interface StateSignals {
  pending: Signal<ThesisRevisionsState['pending']>;
  decided: Signal<ThesisRevisionsState['decided']>;
  theses: Signal<ThesisRevisionsState['theses']>;
  isLoading: Signal<boolean>;
  errorMessage: Signal<string>;
}

export function thesisRevisionsComputed(store: StateSignals) {
  const connectivity = inject(ConnectivityService);
  const hasData = computed(() => store.pending().length > 0 || store.decided().length > 0);

  return {
    hasData,
    thesesById: computed(() => new Map(store.theses().map(thesis => [thesis.id, thesis]))),
    // Reloads in place: cards stay under the banner; offline only counts while nothing is on screen.
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
