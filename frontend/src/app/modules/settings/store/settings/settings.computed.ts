import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {InitialsUtils} from '../../../../shared/utils/initials.utils';
import {type AccountClosureOptions} from '../../models/account-closure/account-closure.model';
import {type UserProfile} from '../../models/settings/settings.model';

interface StateSignals {
  profile: Signal<Nullable<UserProfile>>;
  closureOptions: Signal<Nullable<AccountClosureOptions>>;
}

export function settingsComputed(store: StateSignals) {
  return {
    avatarInitials: computed(() => {
      const p = store.profile();
      return p ? InitialsUtils.fromProfile(p.firstName, p.lastName, null) : '?';
    }),
    fullName: computed(() => {
      const p = store.profile();
      return p ? `${p.firstName} ${p.lastName}` : '';
    }),
    canCloseAccount: computed(() => store.closureOptions()?.canClose ?? false),
    closureDescription: computed(() => {
      const options = store.closureOptions();
      if (!options) {
        return 'Close your account and sign out everywhere.';
      }
      if (!options.canClose) {
        return options.reason ?? 'This account cannot be closed right now.';
      }
      return (
        'Close your account and sign out everywhere. Your data is kept for ' +
        `${options.graceDays} days so an owner can reopen it, then erased for good.`
      );
    }),
  };
}
