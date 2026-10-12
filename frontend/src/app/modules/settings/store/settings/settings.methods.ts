import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type UserProfile} from '../../models/settings/settings.model';
import {type SettingsState} from './settings.state';

export function settingsMethods(store: WritableStateSource<SettingsState>) {
  return {
    setProfile(profile: UserProfile): void {
      patchState(store, {profile});
    },
    updateProfile(partial: Partial<UserProfile>): void {
      patchState(store, (s: SettingsState) => ({
        profile: s.profile ? {...s.profile, ...partial} : s.profile,
      }));
    },
    setShowDeleteConfirm(show: boolean): void {
      patchState(store, {showDeleteConfirm: show});
    },
  };
}
