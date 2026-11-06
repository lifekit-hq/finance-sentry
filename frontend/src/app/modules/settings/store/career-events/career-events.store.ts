import {withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {careerEventsComputed} from './career-events.computed';
import {careerEventsEffects, careerEventsHooks} from './career-events.effects';
import {careerEventsMethods} from './career-events.methods';
import {initialCareerEventsState} from './career-events.state';

export const CareerEventsStore = signalStore(
  withState(initialCareerEventsState),
  withNamedAsyncStatus('events', {defaultErrorMessage: 'Failed to load the career timeline.'}),
  withNamedAsyncStatus('save', {defaultErrorMessage: 'Could not save the career event.'}),
  withMethods(careerEventsMethods),
  withComputed(careerEventsComputed),
  withMethods(careerEventsEffects),
  withHooks({onInit: careerEventsHooks})
);
