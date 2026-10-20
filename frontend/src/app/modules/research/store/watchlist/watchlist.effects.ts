import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, filter, mergeMap, pipe, switchMap, tap} from 'rxjs';

import {
  PENDING_ID_PREFIX,
  WATCHLIST_ITEM_NOT_FOUND,
} from '../../constants/watchlist/watchlist.constants';
import {type AddWatchlistRequest, type WatchlistItem} from '../../models/watchlist/watchlist.model';
import {WatchlistService} from '../../services/watchlist.service';
import {type WatchlistAction} from './watchlist.state';

export interface AddWatchlistCommand {
  payload: AddWatchlistRequest;
  onAdded: () => void;
}

interface EffectsStore {
  items: () => WatchlistItem[];
  isLoadLoading: () => boolean;
  setLoadedItems: (items: WatchlistItem[]) => void;
  setLastAction: (action: WatchlistAction) => void;
  appendItem: (item: WatchlistItem) => void;
  replaceItem: (id: string, item: WatchlistItem) => void;
  insertItem: (index: number, item: WatchlistItem) => void;
  removeItem: (id: string) => void;
  setLoadLoading: () => void;
  setLoadSuccess: () => void;
  setLoadError: (errorCode: Nullable<string>) => void;
  setAddLoading: () => void;
  setAddSuccess: () => void;
  setAddError: (errorCode: Nullable<string>) => void;
  setRemoveLoading: () => void;
  setRemoveSuccess: () => void;
  setRemoveError: (errorCode: Nullable<string>) => void;
}

export function watchlistEffects(store: EffectsStore) {
  const service = inject(WatchlistService);

  return {
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoadLoading()),
        switchMap(() =>
          service.getItems().pipe(
            tap(items => {
              store.setLoadedItems(items);
              store.setLoadSuccess();
            }),
            catchError((err: unknown) => {
              store.setLoadError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
    // Optimistic: the row shows at once under a client id, then takes the server's row; a failed
    // add takes it back out.
    add: rxMethod<AddWatchlistCommand>(
      pipe(
        filter(() => !store.isLoadLoading()),
        mergeMap(({payload, onAdded}) => {
          const pendingId = `${PENDING_ID_PREFIX}${crypto.randomUUID()}`;
          store.setLastAction('add');
          store.setAddLoading();
          store.appendItem({id: pendingId, ...payload, addedAt: new Date().toISOString()});
          return service.add(payload).pipe(
            tap(created => {
              store.replaceItem(pendingId, created);
              store.setAddSuccess();
              onAdded();
            }),
            catchError((err: unknown) => {
              store.removeItem(pendingId);
              store.setAddError(extractErrorCode(err));
              return EMPTY;
            })
          );
        })
      )
    ),
    // Optimistic: the row leaves at once and comes back, where it was, if the delete fails (unless the server no longer has it).
    remove: rxMethod<string>(
      pipe(
        filter(() => !store.isLoadLoading()),
        mergeMap(id => {
          const index = store.items().findIndex(i => i.id === id);
          const removed = index === -1 ? null : store.items()[index];
          store.setLastAction('remove');
          store.setRemoveLoading();
          store.removeItem(id);
          return service.remove(id).pipe(
            tap(() => store.setRemoveSuccess()),
            catchError((err: unknown) => {
              const errorCode = extractErrorCode(err);
              if (removed && errorCode !== WATCHLIST_ITEM_NOT_FOUND) {
                store.insertItem(index, removed);
              }
              store.setRemoveError(errorCode);
              return EMPTY;
            })
          );
        })
      )
    ),
  };
}

interface HookStore extends EffectsStore {
  load: () => void;
}

export function watchlistHooks(store: HookStore): void {
  store.load();
}
