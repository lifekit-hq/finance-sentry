import {inject} from '@angular/core';
import {getState, patchState, withHooks, type WritableStateSource} from '@ngrx/signals';

import {SessionCacheStore} from './session-cache.store';

/**
 * Restores the fields a page store last held when the page opens again, and keeps them when it
 * closes. Compose it after `withUrlSync` and before the store's own load hook: the restored data
 * paints at once and the load revalidates it. `restoreIf` refuses a snapshot that no longer fits
 * the current state (rows loaded for a different filter than the URL now asks for); `onRestore`
 * is patched in with a restored snapshot (a flag the page reads to revalidate without dimming).
 */
export function withSessionCache<State extends object>(
  key: string,
  fields: readonly (keyof State)[],
  restoreIf: (kept: Partial<State>, current: State) => boolean = () => true,
  onRestore: Partial<State> = {}
) {
  // Typed by the caller's `State`: the hooks only read and patch the fields it names.
  const source = (store: object): WritableStateSource<State> => store as WritableStateSource<State>;
  return withHooks(store => {
    const cache = inject(SessionCacheStore);
    const state = source(store);
    return {
      onInit(): void {
        const kept = cache.read<Partial<State>>(key);
        if (kept && restoreIf(kept, getState(state))) {
          patchState(state, {...kept, ...onRestore});
        }
      },
      onDestroy(): void {
        const current = getState(state);
        const kept: Partial<State> = {};
        for (const field of fields) {
          kept[field] = current[field];
        }
        cache.write(key, kept);
      },
    };
  });
}
