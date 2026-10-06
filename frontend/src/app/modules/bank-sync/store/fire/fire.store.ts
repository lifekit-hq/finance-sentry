import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {fireComputed} from './fire.computed';
import {fireEffects, fireHooks} from './fire.effects';
import {fireMethods} from './fire.methods';
import {initialFireState} from './fire.state';

export const FireStore = signalStore(
  withState(initialFireState),
  withMethods(fireMethods),
  withComputed(fireComputed),
  withMethods(fireEffects),
  withHooks({onInit: fireHooks})
);
