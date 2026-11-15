import {type EnvironmentProviders, inject, makeEnvironmentProviders} from '@angular/core';
import {CMN_WHATS_NEW_CONFIG, type WhatsNewConfig} from '@lifekit-hq/ui';

import {AuthStore} from '../../modules/auth/store/auth.store';
import {OWNER_ROLE} from '../../shared/constants/role/role.constants';
import {APP_VERSION} from '../../shared/constants/version/version.constants';

/** localStorage key holding the app version this device last opened What's new for. */
export const WHATS_NEW_STORAGE_KEY = 'fns.whatsNew.lastSeen';

/**
 * What's new from `@lifekit-hq/ui`: the notes `lifekit-whats-new` builds into `public/whats-new.json`,
 * with the `(owner)` lines left for the Owner role only.
 */
export function provideAppWhatsNew(): EnvironmentProviders {
  return makeEnvironmentProviders([
    {
      provide: CMN_WHATS_NEW_CONFIG,
      useFactory: (): WhatsNewConfig => {
        const roles = inject(AuthStore).roles;
        return {
          version: APP_VERSION,
          storageKey: WHATS_NEW_STORAGE_KEY,
          includeOwnerNotes: () => roles().includes(OWNER_ROLE),
        };
      },
    },
  ]);
}
