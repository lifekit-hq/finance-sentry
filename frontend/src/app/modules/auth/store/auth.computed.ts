import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {Permission} from '../../../shared/enums/permission/permission.enum';
import {InitialsUtils} from '../../../shared/utils/initials.utils';

interface StateSignals {
  userId: Signal<Nullable<string>>;
  email: Signal<Nullable<string>>;
  firstName: Signal<Nullable<string>>;
  lastName: Signal<Nullable<string>>;
  permissions: Signal<string[]>;
}

export function authComputed(store: StateSignals) {
  return {
    avatarInitials: computed(() =>
      InitialsUtils.fromProfile(store.firstName(), store.lastName(), store.email())
    ),
    fullName: computed(() =>
      [store.firstName(), store.lastName()].filter((part): part is string => !!part).join(' ')
    ),
    isAuthenticated: computed(() => store.userId() !== null),
    canUseAi: computed(() => store.permissions().includes(Permission.AiUse)),
    canManageUsers: computed(() => store.permissions().includes(Permission.UsersManage)),
  };
}
