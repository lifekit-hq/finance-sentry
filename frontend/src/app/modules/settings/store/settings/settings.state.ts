import {type UserProfile} from '../../models/settings/settings.model';

export interface SettingsState {
  profile: Nullable<UserProfile>;
  // Bumped when a rejected change is rolled back, so a field showing the rejected text can resync.
  profileRevision: number;
  showDeleteConfirm: boolean;
}

export const initialSettingsState: SettingsState = {
  profile: null,
  profileRevision: 0,
  showDeleteConfirm: false,
};
