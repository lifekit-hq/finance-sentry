import {computed, type Signal} from '@angular/core';

import {PaletteEntityUtils} from '../../../../core/shell/utils/palette-entity.utils';
import {type WatchlistItem, type WatchlistRow} from '../../models/watchlist/watchlist.model';

interface StateSignals {
  items: Signal<WatchlistItem[]>;
}

export function watchlistComputed(store: StateSignals) {
  return {
    /** Items with their dossier route, newest first. */
    rows: computed((): WatchlistRow[] =>
      [...store.items()]
        .sort((a, b) => b.addedAt.localeCompare(a.addedAt))
        .map(item => ({...item, dossierRoute: PaletteEntityUtils.assetRoute(item.ticker)}))
    ),
  };
}
