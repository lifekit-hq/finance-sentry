import {patchState, type WritableStateSource} from '@ngrx/signals';

import {APP_VERSION} from '../../../shared/constants/version/version.constants';
import {type WhatsNewVersion} from '../models/whats-new.model';
import {WhatsNewSeenUtils} from '../utils/whats-new-seen.utils';
import {type WhatsNewState} from './whats-new.state';

export function whatsNewMethods(store: WritableStateSource<WhatsNewState>) {
  return {
    setLastSeen(lastSeen: string): void {
      patchState(store, {lastSeen});
    },
    /** Opening the panel clears the dot: this device has now seen the running version. */
    markSeen(): void {
      WhatsNewSeenUtils.write(APP_VERSION);
      patchState(store, {lastSeen: APP_VERSION});
    },
    setVersions(versions: WhatsNewVersion[]): void {
      patchState(store, {versions, loaded: true});
    },
  };
}
