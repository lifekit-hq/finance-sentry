import {computed, type Signal} from '@angular/core';

import {type Alert, type AlertFilter} from '../../models/alert/alert.model';
import {type AlertsQuery} from '../../models/alert/alerts-query.model';

interface StateSignals {
  alerts: Signal<Alert[]>;
  filter: Signal<AlertFilter>;
  search: Signal<string>;
  unreadCount: Signal<number>;
  totalCount: Signal<number>;
  currentPage: Signal<number>;
  pageSize: Signal<number>;
}

export function alertsComputed(store: StateSignals) {
  const totalPages = computed(() => Math.max(1, Math.ceil(store.totalCount() / store.pageSize())));
  return {
    totalPages,
    hasPreviousPage: computed(() => store.currentPage() > 1),
    hasNextPage: computed(() => store.currentPage() < totalPages()),
    /** What the list is showing — the load effect refetches whenever it changes. */
    query: computed<AlertsQuery>(() => ({
      filter: store.filter(),
      page: store.currentPage(),
      pageSize: store.pageSize(),
      search: store.search().trim(),
    })),
  };
}
