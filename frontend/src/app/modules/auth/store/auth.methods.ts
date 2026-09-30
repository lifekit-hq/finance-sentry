import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type AuthResponse} from '../models/auth/auth.model';
import {type AuthFlow, type AuthState, type FlashMessage} from './auth.state';

export function authMethods(store: WritableStateSource<AuthState>) {
  return {
    applyAuthResponse(res: AuthResponse): void {
      patchState(store, {
        userId: res.user.id,
        email: res.user.email,
        roles: res.user.roles,
        status: 'idle',
        errorCode: null,
        flow: null,
        flashMessage: null,
      });
    },
    clearSession(): void {
      patchState(store, {
        userId: null,
        email: null,
        roles: [],
        status: 'idle',
        errorCode: null,
        flow: null,
      });
    },
    setLoading(flow: AuthFlow): void {
      patchState(store, {status: 'loading', errorCode: null, flow});
    },
    setError(errorCode: Nullable<string>, flow: AuthFlow): void {
      patchState(store, {status: 'error', errorCode, flow});
    },
    resetError(): void {
      patchState(store, {status: 'idle', errorCode: null});
    },
    setReturnUrl(returnUrl: Nullable<string>): void {
      patchState(store, {returnUrl});
    },
    setFlashMessage(flashMessage: Nullable<FlashMessage>): void {
      patchState(store, {flashMessage});
    },
  };
}
