import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {fxImpactComputed} from './fx-impact.computed';
import {fxImpactEffects, fxImpactHooks} from './fx-impact.effects';
import {fxImpactMethods} from './fx-impact.methods';
import {initialFxImpactState} from './fx-impact.state';

export const FxImpactStore = signalStore(
  withState(initialFxImpactState),
  withMethods(fxImpactMethods),
  withComputed(fxImpactComputed),
  withMethods(fxImpactEffects),
  withHooks({onInit: fxImpactHooks})
);
