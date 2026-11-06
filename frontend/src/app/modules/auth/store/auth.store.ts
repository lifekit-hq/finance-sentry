import {withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {authComputed} from './auth.computed';
import {authEffects, authHooks} from './auth.effects';
import {authMethods} from './auth.methods';
import {initialAuthState} from './auth.state';

export const AuthStore = signalStore(
  {providedIn: 'root'},
  withState(initialAuthState),
  // `error` keeps the login honest: it offers a Retry and the OIDC path, never a guessed method set.
  withNamedAsyncStatus('signInMethods'),
  withMethods(authMethods),
  withComputed(authComputed),
  withMethods(authEffects),
  withHooks({onInit: authHooks})
);
