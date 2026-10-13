import {accountPreferencesStore} from '@lifekit-hq/core/preferences';

import {environment} from '../../../../environments/environment';

/**
 * The signed-in person's account-tier preferences (`GET`/`PATCH /api/v1/me/preferences`). Today that is
 * the appearance (theme mode, seed colour, intensity); `AppearanceService` writes it, the shell loads it
 * after sign-in and clears it on sign-out. The path carries the API prefix because the kit store uses its
 * endpoint as given.
 */
export const AppPreferencesStore = accountPreferencesStore(
  `${environment.apiBaseUrl}/me/preferences`
);
