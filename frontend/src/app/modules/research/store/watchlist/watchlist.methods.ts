import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type WatchlistItem} from '../../models/watchlist/watchlist.model';
import {type WatchlistState} from './watchlist.state';

export function watchlistMethods(store: WritableStateSource<WatchlistState>) {
  return {
    setItems(items: WatchlistItem[]): void {
      patchState(store, {items});
    },
    appendItem(item: WatchlistItem): void {
      patchState(store, (s: WatchlistState) => ({items: [...s.items, item]}));
    },
    replaceItem(id: string, item: WatchlistItem): void {
      patchState(store, (s: WatchlistState) => ({
        items: s.items.map(i => (i.id === id ? item : i)),
      }));
    },
    removeItem(id: string): void {
      patchState(store, (s: WatchlistState) => ({items: s.items.filter(i => i.id !== id)}));
    },
  };
}
