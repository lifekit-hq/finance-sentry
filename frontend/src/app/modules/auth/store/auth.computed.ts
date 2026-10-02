import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {Permission} from '../../../shared/enums/permission/permission.enum';
import {InitialsUtils} from '../../../shared/utils/initials.utils';
import {type AuthFlow} from './auth.state';

interface StateSignals {
  userId: Signal<Nullable<string>>;
  email: Signal<Nullable<string>>;
  firstName: Signal<Nullable<string>>;
  lastName: Signal<Nullable<string>>;
  permissions: Signal<string[]>;
  status: Signal<AsyncStatus>;
  errorCode: Signal<Nullable<string>>;
  errorDetail: Signal<Nullable<string>>;
  flow: Signal<AuthFlow>;
}

const ACCOUNT_LOCKED_CODE = 'ACCOUNT_LOCKED';

function flowFallback(flow: AuthFlow): string {
  switch (flow) {
    case 'login':
      return 'Invalid email or password.';
    case 'acceptInvite':
      return 'Could not set your password. Try again or ask the owner for a new invite.';
    default:
      return '';
  }
}

export function authComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  return {
    avatarInitials: computed(() =>
      InitialsUtils.fromProfile(store.firstName(), store.lastName(), store.email())
    ),
    isAuthenticated: computed(() => store.userId() !== null),
    canUseAi: computed(() => store.permissions().includes(Permission.AiUse)),
    canManageUsers: computed(() => store.permissions().includes(Permission.UsersManage)),
    isLoading: computed(() => store.status() === 'loading'),
    errorMessage: computed(() => {
      const code = store.errorCode();
      if (!code) {
        return '';
      }
      const detail = code === ACCOUNT_LOCKED_CODE ? store.errorDetail() : null;
      return errorMessages.resolve(code) ?? detail ?? flowFallback(store.flow());
    }),
  };
}
