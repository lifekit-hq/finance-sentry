import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {InitialsUtils} from '../../../../shared/utils/initials.utils';
import {type UserProfile} from '../../models/settings/settings.model';

interface StateSignals {
  profile: Signal<Nullable<UserProfile>>;
  profileSaving: Signal<boolean>;
  profileErrorCode: Signal<Nullable<string>>;
  passwordSaving: Signal<boolean>;
  passwordErrorCode: Signal<Nullable<string>>;
  showDeleteConfirm: Signal<boolean>;
  errorCode: Signal<Nullable<string>>;
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
    loadErrorMessage: computed(
      () => errorMessages.resolve(store.errorCode()) ?? 'Failed to load profile.'
    ),
    profileSaveErrorMessage: computed(
      () => errorMessages.resolve(store.profileErrorCode()) ?? 'Failed to save profile.'
    ),
    passwordSaveErrorMessage: computed(
      () => errorMessages.resolve(store.passwordErrorCode()) ?? 'Failed to change password.'
    ),
  };
}
