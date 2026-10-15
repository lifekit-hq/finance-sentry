import {withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {watchlistComputed} from './watchlist.computed';
import {watchlistEffects, watchlistHooks} from './watchlist.effects';
import {watchlistMethods} from './watchlist.methods';
import {initialWatchlistState} from './watchlist.state';

export const WatchlistStore = signalStore(
  withState(initialWatchlistState),
  withNamedAsyncStatus('load', {defaultErrorMessage: 'Failed to load your watchlist.'}),
  withNamedAsyncStatus('add', {defaultErrorMessage: 'Failed to add to your watchlist.'}),
  withNamedAsyncStatus('remove', {defaultErrorMessage: 'Failed to remove from your watchlist.'}),
  withMethods(watchlistMethods),
  withComputed(watchlistComputed),
  withMethods(watchlistEffects),
  withHooks({onInit: watchlistHooks})
);
