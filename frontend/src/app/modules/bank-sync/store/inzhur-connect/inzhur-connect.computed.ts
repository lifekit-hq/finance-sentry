import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';
import {type AsyncStateStatus} from '@lifekit-hq/ui';

import {type InzhurConnectState, type InzhurLoadStatus} from './inzhur-connect.state';

interface StateSignals {
  connection: Signal<InzhurConnectState['connection']>;
  statusLoad: Signal<InzhurLoadStatus>;
  editingCredentials: Signal<boolean>;
  starting: Signal<boolean>;
  errorCode: Signal<Nullable<string>>;
}

const DEFAULT_START_ERROR = "Couldn't sign in to Inzhur. Please try again.";
const LOAD_ERROR = "Couldn't load your Inzhur connection. Close this window and try again.";

const ASYNC_STATUS: Record<InzhurLoadStatus, AsyncStateStatus> = {
  loading: 'loading',
  loaded: 'success',
  error: 'error',
};

export function inzhurConnectComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  const hasSavedCredentials = computed(() => store.connection()?.hasSavedCredentials ?? false);

  return {
    asyncStatus: computed(() => ASYNC_STATUS[store.statusLoad()]),
    loadErrorMessage: computed(() => (store.statusLoad() === 'error' ? LOAD_ERROR : '')),
    hasSavedCredentials,
    isReconnect: computed(() => store.connection()?.status === 'reauth_required'),
    loginAvailable: computed(() => store.connection()?.loginAvailable ?? false),
    /** Phone and password are asked for unless saved ones exist and the owner keeps them. */
    asksForCredentials: computed(() => !hasSavedCredentials() || store.editingCredentials()),
    isStarting: computed(() => store.starting()),
    startErrorMessage: computed(() => {
      const code = store.errorCode();
      if (!code) {
        return '';
      }
      return errorMessages.resolve(code) ?? DEFAULT_START_ERROR;
    }),
  };
}
