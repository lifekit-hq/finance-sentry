import {type UserProfile} from '../../models/settings/settings.model';

export interface SettingsState {
  profile: Nullable<UserProfile>;
  showDeleteConfirm: boolean;
}

export const initialSettingsState: SettingsState = {
  profile: null,
  showDeleteConfirm: false,
};
