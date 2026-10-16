import {withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {eventsComputed} from './events.computed';
import {eventsEffects, eventsHooks} from './events.effects';
import {eventsMethods} from './events.methods';
import {initialEventsState} from './events.state';

export const EventsStore = signalStore(
  withState(initialEventsState),
  withNamedAsyncStatus('upcoming', {defaultErrorMessage: 'Failed to load upcoming events.'}),
  withNamedAsyncStatus('fired', {defaultErrorMessage: 'Failed to load fired events.'}),
  // The next fired page appends under the rows on screen; `lk-infinite-list` draws its state.
  withNamedAsyncStatus('firedAppend', {defaultErrorMessage: "Couldn't load more events."}),
  withMethods(eventsMethods),
  withComputed(eventsComputed),
  withMethods(eventsEffects),
  withHooks({onInit: eventsHooks})
);
