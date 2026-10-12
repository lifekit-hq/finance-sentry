import {type UserProfile} from '../../models/settings/settings.model';

export interface SettingsState {
  profile: Nullable<UserProfile>;
  profileSaving: boolean;
  profileErrorCode: Nullable<string>;
  showDeleteConfirm: boolean;
}

export const initialSettingsState: SettingsState = {
  profile: null,
  profileSaving: false,
  profileErrorCode: null,
  showDeleteConfirm: false,
};
