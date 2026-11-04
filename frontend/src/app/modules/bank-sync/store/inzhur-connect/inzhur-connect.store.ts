import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {inzhurConnectComputed} from './inzhur-connect.computed';
import {inzhurConnectEffects, inzhurConnectHooks} from './inzhur-connect.effects';
import {initialInzhurConnectState} from './inzhur-connect.state';

/** Page-scoped: provided on the Inzhur form so it tears down with the modal step. */
export const InzhurConnectStore = signalStore(
  withState(initialInzhurConnectState),
  withAsyncStatus({
    defaultErrorMessage: "Couldn't load your Inzhur connection. Close this window and try again.",
  }),
  withComputed(inzhurConnectComputed),
  withMethods(inzhurConnectEffects),
  withHooks({onInit: inzhurConnectHooks})
);
