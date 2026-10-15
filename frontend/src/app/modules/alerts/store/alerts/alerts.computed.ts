import {computed, type Signal} from '@angular/core';

import {type Alert, type AlertFilter} from '../../models/alert/alert.model';
import {type AlertsQuery} from '../../models/alert/alerts-query.model';

interface StateSignals {
  alerts: Signal<Alert[]>;
  filter: Signal<AlertFilter>;
  search: Signal<string>;
  unreadCount: Signal<number>;
  totalCount: Signal<number>;
}

export function alertsComputed(store: StateSignals) {
  return {
    hasMore: computed(() => store.alerts().length < store.totalCount()),
    /** What the list is filtered by — the load effect refetches the first page whenever it changes. */
    query: computed<AlertsQuery>(() => ({
      filter: store.filter(),
      search: store.search().trim(),
    })),
  };
}
