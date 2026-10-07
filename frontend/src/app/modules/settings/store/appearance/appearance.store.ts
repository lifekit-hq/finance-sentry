import {signalStore, withHooks, withMethods, withState} from '@ngrx/signals';

import {appearanceEffects, appearanceHooks} from './appearance.effects';
import {appearanceMethods} from './appearance.methods';
import {initialAppearanceState} from './appearance.state';

export const AppearanceStore = signalStore(
  withState(initialAppearanceState),
  withMethods(appearanceMethods),
  withMethods(appearanceEffects),
  withHooks({onInit: appearanceHooks})
);
