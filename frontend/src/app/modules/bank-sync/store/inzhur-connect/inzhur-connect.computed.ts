import {computed, type Signal} from '@angular/core';

import {type InzhurConnectState} from './inzhur-connect.state';

interface StateSignals {
  connection: Signal<InzhurConnectState['connection']>;
}

export function inzhurConnectComputed(store: StateSignals) {
  return {
    isReconnect: computed(() => store.connection()?.status === 'reauth_required'),
  };
}
