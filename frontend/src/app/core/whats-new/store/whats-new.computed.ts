import {computed, inject, type Signal} from '@angular/core';
import {type AsyncStateStatus} from '@lifekit-hq/ui';

import {AuthStore} from '../../../modules/auth/store/auth.store';
import {OWNER_ROLE} from '../../../shared/constants/role/role.constants';
import {APP_VERSION} from '../../../shared/constants/version/version.constants';
import {VersionUtils} from '../../../shared/utils/version.utils';
import {WHATS_NEW_ERROR_MESSAGE} from '../constants/whats-new.constants';
import {type WhatsNewVersion} from '../models/whats-new.model';
import {type WhatsNewState} from './whats-new.state';

interface StateSignals {
  lastSeen: Signal<WhatsNewState['lastSeen']>;
  versions: Signal<WhatsNewState['versions']>;
  loaded: Signal<WhatsNewState['loaded']>;
  status: Signal<WhatsNewState['status']>;
}

export function whatsNewComputed(store: StateSignals) {
  const roles = inject(AuthStore).roles;

  const isOwner = computed(() => roles().includes(OWNER_ROLE));
  /** Each version with the notes this person may read: an owner line is hidden from Members. */
  const visibleVersions = computed<WhatsNewVersion[]>(() =>
    store.versions().map(version => ({
      ...version,
      notes: version.notes.filter(note => isOwner() || !note.owner),
    }))
  );
  const status = computed<AsyncStateStatus>(() => {
    if (store.status() === 'loading' && !store.loaded()) {
      return 'loading';
    }
    return store.status() === 'error' && !store.loaded() ? 'error' : 'success';
  });

  return {
    isOwner,
    visibleVersions,
    viewStatus: status,
    errorMessage: computed(() => (status() === 'error' ? WHATS_NEW_ERROR_MESSAGE : '')),
    /** The running version is newer than the one last seen on this device and has notes this person can read. */
    hasUnread: computed(() => {
      const lastSeen = store.lastSeen();
      if (lastSeen === null || !VersionUtils.isNewer(APP_VERSION, lastSeen)) {
        return false;
      }
      const current = visibleVersions().find(version => version.version === APP_VERSION);
      return (current?.notes.length ?? 0) > 0;
    }),
  };
}
