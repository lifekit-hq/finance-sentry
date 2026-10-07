import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {inzhurConnectComputed} from './inzhur-connect.computed';
import {inzhurConnectEffects, inzhurConnectHooks} from './inzhur-connect.effects';
import {inzhurConnectMethods} from './inzhur-connect.methods';
import {initialInzhurConnectState} from './inzhur-connect.state';

/** Page-scoped: provided on the Inzhur form so it tears down with the modal step. */
export const InzhurConnectStore = signalStore(
  withState(initialInzhurConnectState),
  withMethods(inzhurConnectMethods),
  withComputed(inzhurConnectComputed),
  withMethods(inzhurConnectEffects),
  withHooks({onInit: inzhurConnectHooks})
);
