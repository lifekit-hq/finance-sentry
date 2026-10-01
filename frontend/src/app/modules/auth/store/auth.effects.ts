import {effect, inject, type Signal, untracked} from '@angular/core';
import {NavigationEnd, Router} from '@angular/router';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, filter, pipe, startWith, switchMap, tap} from 'rxjs';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {ErrorUtils} from '../../../shared/utils/error.utils';
import {
  type AcceptInviteRequest,
  type AuthRequest,
  type AuthResponse,
} from '../models/auth/auth.model';
import {AuthService} from '../services/auth.service';
import {type AuthFlow, type FlashMessage} from './auth.state';

interface EffectsStore {
  applyAuthResponse: (res: AuthResponse) => void;
  clearSession: () => void;
  setLoading: (flow: AuthFlow) => void;
  setError: (errorCode: Nullable<string>, flow: AuthFlow) => void;
  setReturnUrl: (returnUrl: Nullable<string>) => void;
  setFlashMessage: (flashMessage: Nullable<FlashMessage>) => void;
  isAuthenticated: Signal<boolean>;
  returnUrl: Signal<Nullable<string>>;
}

function flashFromParams(info: Nullable<string>, error: Nullable<string>): Nullable<FlashMessage> {
  if (info === 'google_cancelled') {
    return {kind: 'info', text: 'Google sign-in was cancelled. Try again or use email/password.'};
  }
  if (error === 'google_failed') {
    return {kind: 'error', text: 'Google sign-in failed. Please try again.'};
  }
  return null;
}

export function authEffects(store: EffectsStore) {
  const authService = inject(AuthService);
  const router = inject(Router);

  return {
    login: rxMethod<AuthRequest>(
      pipe(
        tap(() => store.setLoading('login')),
        switchMap(req =>
          authService.login(req).pipe(
            tap(res => store.applyAuthResponse(res)),
            catchError((err: unknown) => {
              store.setError(ErrorUtils.extractCode(err), 'login');
              return EMPTY;
            })
          )
        )
      )
    ),
    acceptInvite: rxMethod<AcceptInviteRequest>(
      pipe(
        tap(() => store.setLoading('acceptInvite')),
        switchMap(req =>
          authService.acceptInvite(req).pipe(
            tap(res => store.applyAuthResponse(res)),
            catchError((err: unknown) => {
              store.setError(ErrorUtils.extractCode(err), 'acceptInvite');
              return EMPTY;
            })
          )
        )
      )
    ),
    verifyGoogleCredential: rxMethod<string>(
      pipe(
        tap(() => store.setLoading('google')),
        switchMap(credential =>
          authService.verifyGoogleCredential(credential).pipe(
            tap(res => store.applyAuthResponse(res)),
            catchError((err: unknown) => {
              store.setError(ErrorUtils.extractCode(err), 'google');
              return EMPTY;
            })
          )
        )
      )
    ),
    logout(): void {
      authService.logout().subscribe({error: () => undefined});
      store.clearSession();
      void router.navigate([AppRoute.Login]);
    },
  };
}

export function authHooks(store: EffectsStore): void {
  const router = inject(Router);

  router.events
    .pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      startWith(null)
    )
    .subscribe(() => {
      const params = router.routerState.root.snapshot.queryParamMap;
      store.setReturnUrl(params.get('returnUrl'));
      store.setFlashMessage(flashFromParams(params.get('info'), params.get('error')));
    });

  effect(() => {
    if (!store.isAuthenticated()) {
      return;
    }
    untracked(() => {
      const target = store.returnUrl() ?? AppRoute.Accounts;
      const currentPath = router.url.split('?')[0];
      const loginPath: string = AppRoute.Login;
      const acceptInvitePath: string = AppRoute.AcceptInvite;
      if (currentPath === loginPath || currentPath === acceptInvitePath) {
        void router.navigateByUrl(target);
      }
    });
  });
}
