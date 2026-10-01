import {computed, type Signal} from '@angular/core';

import {type Alert, type AlertFilter} from '../../models/alert/alert.model';

interface StateSignals {
  alerts: Signal<Alert[]>;
  filter: Signal<AlertFilter>;
  unreadCount: Signal<number>;
  status: Signal<AsyncStatus>;
}

export function alertsComputed(store: StateSignals) {
  return {
    isLoading: computed(() => store.status() === 'loading'),
    errorMessage: computed(() => (store.status() === 'error' ? 'Failed to load alerts.' : null)),
  };
}
