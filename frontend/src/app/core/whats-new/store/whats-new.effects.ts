import {inject, type Signal} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, filter, pipe, switchMap, tap} from 'rxjs';

import {APP_VERSION} from '../../../shared/constants/version/version.constants';
import {VersionUtils} from '../../../shared/utils/version.utils';
import {type WhatsNewVersion} from '../models/whats-new.model';
import {WhatsNewService} from '../services/whats-new.service';
import {WhatsNewSeenUtils} from '../utils/whats-new-seen.utils';
import {type WhatsNewState} from './whats-new.state';

interface StoreMethods {
  loaded: Signal<WhatsNewState['loaded']>;
  setLoading(): void;
  setVersions(versions: WhatsNewVersion[]): void;
  setError(): void;
}

export function whatsNewEffects(store: StoreMethods) {
  const service = inject(WhatsNewService);

  /** Fetches `whats-new.json` once; a retry after an error asks again. */
  const load = rxMethod<void>(
    pipe(
      filter(() => !store.loaded()),
      tap(() => store.setLoading()),
      switchMap(() =>
        service.load().pipe(
          tap(data => store.setVersions(data.versions)),
          catchError(() => {
            store.setError();
            return EMPTY;
          })
        )
      )
    )
  );

  return {load};
}

interface HookStore {
  setLastSeen(lastSeen: string): void;
  load(): void;
}

export function whatsNewHooks(store: HookStore) {
  return {
    onInit: () => {
      const stored = WhatsNewSeenUtils.read();
      if (stored === null) {
        // First run on this device: the running version counts as seen, so no dot on first sign-in.
        WhatsNewSeenUtils.write(APP_VERSION);
        store.setLastSeen(APP_VERSION);
        return;
      }
      store.setLastSeen(stored);
      if (VersionUtils.isNewer(APP_VERSION, stored)) {
        // The dot needs to know whether this release has notes; this is the one fetch outside the panel.
        store.load();
      }
    },
  };
}
