import {type WatchlistItem} from '../../models/watchlist/watchlist.model';

export interface WatchlistState {
  items: WatchlistItem[];
}

export const initialWatchlistState: WatchlistState = {
  items: [],
};
