import {withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {universeScreenComputed} from './universe-screen.computed';
import {universeScreenEffects, universeScreenHooks} from './universe-screen.effects';
import {universeScreenMethods} from './universe-screen.methods';
import {initialUniverseScreenState} from './universe-screen.state';

export const UniverseScreenStore = signalStore(
  withState(initialUniverseScreenState),
  withNamedAsyncStatus('load', {defaultErrorMessage: 'Failed to load the universe.'}),
  withMethods(universeScreenMethods),
  withComputed(universeScreenComputed),
  withMethods(universeScreenEffects),
  withHooks({onInit: universeScreenHooks})
);
