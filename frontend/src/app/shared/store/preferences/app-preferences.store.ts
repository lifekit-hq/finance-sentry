import {accountPreferencesStore} from '@lifekit-hq/core/preferences';

import {environment} from '../../../../environments/environment';
import {type AppPreferences} from '../../models/preferences/app-preferences.model';

/**
 * The signed-in person's account-tier preferences (`GET`/`PATCH /api/v1/me/preferences`). Today that is
 * the appearance (theme mode, seed colour, intensity) and how a verdict is shown (`verdictDisplay`);
 * `AppearanceService` writes the appearance, the shell loads it after sign-in and clears it on sign-out. The path carries the API prefix because the kit store uses its
 * endpoint as given.
 */
export const AppPreferencesStore = accountPreferencesStore<AppPreferences>(
  `${environment.apiBaseUrl}/me/preferences`
);
