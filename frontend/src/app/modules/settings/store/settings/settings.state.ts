import {type UserProfile} from '../../models/settings/settings.model';

export interface SettingsState {
  profile: Nullable<UserProfile>;
  profileSaving: boolean;
  profileErrorCode: Nullable<string>;
  showDeleteConfirm: boolean;
  status: AsyncStatus;
  errorCode: Nullable<string>;
}

export const initialSettingsState: SettingsState = {
  profile: null,
  profileSaving: false,
  profileErrorCode: null,
  showDeleteConfirm: false,
  status: 'idle',
  errorCode: null,
};
