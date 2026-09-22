import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {eventsComputed} from './events.computed';
import {eventsEffects, eventsHooks} from './events.effects';
import {eventsMethods} from './events.methods';
import {initialEventsState} from './events.state';

export const EventsStore = signalStore(
  withState(initialEventsState),
  withMethods(eventsMethods),
  withComputed(eventsComputed),
  withMethods(eventsEffects),
  withHooks({onInit: eventsHooks})
);
