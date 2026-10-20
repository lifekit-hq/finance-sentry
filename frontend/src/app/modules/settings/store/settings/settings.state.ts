import {type Nullable} from '@lifekit-hq/ui';

import {type AccountClosureOptions} from '../../models/account-closure/account-closure.model';
import {type UserProfile} from '../../models/settings/settings.model';

export interface SettingsState {
  profile: Nullable<UserProfile>;
  // Bumped when a rejected change is rolled back, so a field showing the rejected text can resync.
  profileRevision: number;
  // Whether this person may close their account; null until the server has answered.
  closureOptions: Nullable<AccountClosureOptions>;
}

export const initialSettingsState: SettingsState = {
  profile: null,
  profileRevision: 0,
  closureOptions: null,
};
