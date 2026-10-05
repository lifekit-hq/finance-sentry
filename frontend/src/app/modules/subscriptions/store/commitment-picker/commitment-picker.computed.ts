import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

interface StateSignals {
  status: Signal<AsyncStatus>;
  errorCode: Signal<Nullable<string>>;
}

export function commitmentPickerComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);
  return {
    isLoading: computed(() => store.status() === 'loading'),
    errorMessage: computed(() =>
      store.status() === 'error'
        ? (errorMessages.resolve(store.errorCode()) ?? 'Failed to load transactions.')
        : ''
    ),
  };
}
