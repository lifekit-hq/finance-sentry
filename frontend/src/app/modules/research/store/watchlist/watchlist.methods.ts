import {patchState, type WritableStateSource} from '@ngrx/signals';

import {PENDING_ID_PREFIX} from '../../constants/watchlist/watchlist.constants';
import {type WatchlistItem} from '../../models/watchlist/watchlist.model';
import {type WatchlistAction, type WatchlistState} from './watchlist.state';

export function watchlistMethods(store: WritableStateSource<WatchlistState>) {
  return {
    setLoadedItems(items: WatchlistItem[]): void {
      patchState(store, (s: WatchlistState) => ({
        items: [...items, ...s.items.filter(i => i.id.startsWith(PENDING_ID_PREFIX))],
      }));
    },
    setLastAction(lastAction: WatchlistAction): void {
      patchState(store, {lastAction});
    },
    appendItem(item: WatchlistItem): void {
      patchState(store, (s: WatchlistState) => ({items: [...s.items, item]}));
    },
    replaceItem(id: string, item: WatchlistItem): void {
      patchState(store, (s: WatchlistState) => ({
        items: s.items.some(i => i.id === id)
          ? s.items.map(i => (i.id === id ? item : i))
          : [...s.items, item],
      }));
    },
    insertItem(index: number, item: WatchlistItem): void {
      patchState(store, (s: WatchlistState) => {
        if (s.items.some(i => i.id === item.id)) {
          return s;
        }
        const items = [...s.items];
        items.splice(Math.min(index, items.length), 0, item);
        return {items};
      });
    },
    removeItem(id: string): void {
      patchState(store, (s: WatchlistState) => ({items: s.items.filter(i => i.id !== id)}));
    },
  };
}
