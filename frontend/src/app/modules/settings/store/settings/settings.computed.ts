import {computed, type Signal} from '@angular/core';

import {InitialsUtils} from '../../../../shared/utils/initials.utils';
import {type UserProfile} from '../../models/settings/settings.model';

interface StateSignals {
  profile: Signal<Nullable<UserProfile>>;
  showDeleteConfirm: Signal<boolean>;
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
  };
}
