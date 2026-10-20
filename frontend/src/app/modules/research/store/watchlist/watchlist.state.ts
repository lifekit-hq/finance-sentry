import {type Nullable} from '@lifekit-hq/ui';

import {type WatchlistItem} from '../../models/watchlist/watchlist.model';

export type WatchlistAction = 'add' | 'remove';

export interface WatchlistState {
  items: WatchlistItem[];
  lastAction: Nullable<WatchlistAction>;
}

export const initialWatchlistState: WatchlistState = {
  items: [],
  lastAction: null,
};
