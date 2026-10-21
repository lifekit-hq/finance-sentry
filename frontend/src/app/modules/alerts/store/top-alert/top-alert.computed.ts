import {computed, type Signal} from '@angular/core';

interface StateSignals {
  unreadCount: Signal<number>;
}

export function topAlertComputed(store: StateSignals) {
  return {
    /** Unread alerts beyond the one shown. */
    moreCount: computed(() => Math.max(0, store.unreadCount() - 1)),
  };
}
