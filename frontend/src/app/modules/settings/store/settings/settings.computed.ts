import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {InitialsUtils} from '../../../../shared/utils/initials.utils';
import {type UserProfile} from '../../models/settings/settings.model';

interface StateSignals {
  profile: Signal<Nullable<UserProfile>>;
  profileSaving: Signal<boolean>;
  profileErrorCode: Signal<Nullable<string>>;
  showDeleteConfirm: Signal<boolean>;
}

export function settingsComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  return {
    avatarInitials: computed(() => {
      const p = store.profile();
      return p ? InitialsUtils.fromProfile(p.firstName, p.lastName, null) : '?';
    }),
    fullName: computed(() => {
      const p = store.profile();
      return p ? `${p.firstName} ${p.lastName}` : '';
    }),
    profileSaveErrorMessage: computed(
      () => errorMessages.resolve(store.profileErrorCode()) ?? 'Failed to save profile.'
    ),
  };
}
