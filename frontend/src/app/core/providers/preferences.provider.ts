import {type EnvironmentProviders, makeEnvironmentProviders} from '@angular/core';
import {provideAppearanceSync, provideToastPreferenceErrors} from '@lifekit-hq/ui';

import {AppPreferencesStore} from '../../shared/store/preferences/app-preferences.store';

/**
 * Appearance follows the person: `AppearanceService` writes every change to the account and the
 * server's copy wins once `AppPreferencesStore` has loaded. A rejected save rolls back and toasts.
 */
export function provideAppPreferences(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppearanceSync(AppPreferencesStore),
    provideToastPreferenceErrors(),
  ]);
}
