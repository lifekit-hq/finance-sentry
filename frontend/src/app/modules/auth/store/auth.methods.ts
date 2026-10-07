import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type AuthResponse, type SignInMethods} from '../models/auth/auth.model';
import {
  type AuthFlow,
  type AuthState,
  type FlashMessage,
  type SignInMethodsStatus,
} from './auth.state';

export function authMethods(store: WritableStateSource<AuthState>) {
  return {
    applyAuthResponse(res: AuthResponse): void {
      patchState(store, {
        userId: res.user.id,
        email: res.user.email,
        roles: res.user.roles,
        permissions: res.user.permissions,
        status: 'idle',
        errorCode: null,
        errorDetail: null,
        flow: null,
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
        status: 'idle',
        errorCode: null,
        errorDetail: null,
        flow: null,
      });
    },
    setProfileName(firstName: Nullable<string>, lastName: Nullable<string>): void {
      patchState(store, {firstName, lastName});
    },
    setLoading(flow: AuthFlow): void {
      patchState(store, {status: 'loading', errorCode: null, errorDetail: null, flow});
    },
    setError(
      errorCode: Nullable<string>,
      flow: AuthFlow,
      errorDetail: Nullable<string> = null
    ): void {
      patchState(store, {status: 'error', errorCode, errorDetail, flow});
    },
    resetError(): void {
      patchState(store, {status: 'idle', errorCode: null, errorDetail: null});
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
