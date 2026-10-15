import {computed, type Signal} from '@angular/core';

import {PaletteEntityUtils} from '../../../../core/shell/utils/palette-entity.utils';
import {type WatchlistItem, type WatchlistRow} from '../../models/watchlist/watchlist.model';
import {type WatchlistAction} from './watchlist.state';

interface StateSignals {
  items: Signal<WatchlistItem[]>;
  lastAction: Signal<Nullable<WatchlistAction>>;
  addErrorMessage: Signal<Nullable<string>>;
  removeErrorMessage: Signal<Nullable<string>>;
}

export function watchlistComputed(store: StateSignals) {
  return {
    /** Items with their dossier route, newest first. */
    rows: computed((): WatchlistRow[] =>
      [...store.items()]
        .sort((a, b) => b.addedAt.localeCompare(a.addedAt))
        .map(item => ({...item, dossierRoute: PaletteEntityUtils.assetRoute(item.ticker)}))
    ),
    /** Error of the most recently started add or remove, so an older failure never lingers. */
    actionErrorMessage: computed((): string => {
      const action = store.lastAction();
      if (action === 'add') {
        return store.addErrorMessage() ?? '';
      }
      return action === 'remove' ? (store.removeErrorMessage() ?? '') : '';
    }),
  };
}
