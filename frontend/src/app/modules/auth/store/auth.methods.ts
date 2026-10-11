import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type AuthResponse, type SignInMethods} from '../models/auth/auth.model';
import {type AuthState, type FlashMessage, type SignInMethodsStatus} from './auth.state';

export function authMethods(store: WritableStateSource<AuthState>) {
  return {
    applyAuthResponse(res: AuthResponse): void {
      patchState(store, {
        userId: res.user.id,
        email: res.user.email,
        roles: res.user.roles,
        permissions: res.user.permissions,
        flashMessage: null,
      });
    },
    clearSession(): void {
      patchState(store, {
        userId: null,
        email: null,
        firstName: null,
        lastName: null,
        roles: [],
        permissions: [],
      });
    },
    setProfileName(firstName: Nullable<string>, lastName: Nullable<string>): void {
      patchState(store, {firstName, lastName});
    },
    setReturnUrl(returnUrl: Nullable<string>): void {
      patchState(store, {returnUrl});
    },
    setSignInMethods(signInMethods: SignInMethods): void {
      patchState(store, {signInMethods, signInMethodsStatus: 'success'});
    },
    setSignInMethodsStatus(signInMethodsStatus: SignInMethodsStatus): void {
      patchState(store, {signInMethodsStatus});
    },
    setFlashMessage(flashMessage: Nullable<FlashMessage>): void {
      patchState(store, {flashMessage});
    },
  };
}
