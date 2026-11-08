import {type Nullable} from '@lifekit-hq/ui';

import {type WatchlistItem} from '../../models/watchlist/watchlist.model';

export type WatchlistAction = 'add' | 'remove';

export interface WatchlistState {
  items: WatchlistItem[];
  lastAction: Nullable<WatchlistAction>;
  /** The owner opened the add form from the empty state; the form is otherwise behind it. */
  isFormOpen: boolean;
}

export const initialWatchlistState: WatchlistState = {
  items: [],
  lastAction: null,
  isFormOpen: false,
};
