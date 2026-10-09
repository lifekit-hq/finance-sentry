import {computed, type Signal} from '@angular/core';
import {type AsyncStateStatus} from '@lifekit-hq/ui';

import {type InzhurConnectState, type InzhurLoadStatus} from './inzhur-connect.state';

interface StateSignals {
  connection: Signal<InzhurConnectState['connection']>;
  statusLoad: Signal<InzhurLoadStatus>;
}

const LOAD_ERROR = "Couldn't load your Inzhur connection. Close this window and try again.";

const ASYNC_STATUS: Record<InzhurLoadStatus, AsyncStateStatus> = {
  loading: 'loading',
  loaded: 'success',
  error: 'error',
};

export function inzhurConnectComputed(store: StateSignals) {
  return {
    asyncStatus: computed(() => ASYNC_STATUS[store.statusLoad()]),
    loadErrorMessage: computed(() => (store.statusLoad() === 'error' ? LOAD_ERROR : '')),
    isReconnect: computed(() => store.connection()?.status === 'reauth_required'),
  };
}
