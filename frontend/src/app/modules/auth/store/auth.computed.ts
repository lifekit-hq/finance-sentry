import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {OWNER_ROLE} from '../../../shared/constants/auth/auth-roles.constants';
import {type AuthFlow} from './auth.state';

interface StateSignals {
  userId: Signal<Nullable<string>>;
  roles: Signal<string[]>;
  status: Signal<AsyncStatus>;
  errorCode: Signal<Nullable<string>>;
  flow: Signal<AuthFlow>;
}

function flowFallback(flow: AuthFlow): string {
  return flow === 'login' ? 'Invalid email or password.' : '';
}

export function authComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  return {
    isAuthenticated: computed(() => store.userId() !== null),
    isOwner: computed(() => store.roles().includes(OWNER_ROLE)),
    isLoading: computed(() => store.status() === 'loading'),
    errorMessage: computed(() => {
      const code = store.errorCode();
      if (!code) {
        return '';
      }
      return errorMessages.resolve(code) ?? flowFallback(store.flow());
    }),
  };
}
