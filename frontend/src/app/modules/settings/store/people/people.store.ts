import {withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {peopleComputed} from './people.computed';
import {peopleEffects, peopleHooks} from './people.effects';
import {peopleMethods} from './people.methods';
import {initialPeopleState} from './people.state';

export const PeopleStore = signalStore(
  withState(initialPeopleState),
  withNamedAsyncStatus('people', {defaultErrorMessage: 'Failed to load people.'}),
  withNamedAsyncStatus('invite', {defaultErrorMessage: 'Could not create the invite.'}),
  withMethods(peopleMethods),
  withComputed(peopleComputed),
  withMethods(peopleEffects),
  withHooks({onInit: peopleHooks})
);
