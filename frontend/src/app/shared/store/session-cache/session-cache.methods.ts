import {getState, patchState, type WritableStateSource} from '@ngrx/signals';

import {type SessionCacheState} from './session-cache.state';

export function sessionCacheMethods(store: WritableStateSource<SessionCacheState>) {
  return {
    read<T>(key: string): T | undefined {
      return getState(store).entries[key] as T | undefined;
    },
    write(key: string, value: unknown): void {
      patchState(store, state =>
        state.closed ? state : {entries: {...state.entries, [key]: value}}
      );
    },
    open(): void {
      patchState(store, {closed: false});
    },
    /** Sign-out: the next person on this tab never sees the previous one's numbers. */
    clear(): void {
      patchState(store, {entries: {}, closed: true});
    },
  };
}
