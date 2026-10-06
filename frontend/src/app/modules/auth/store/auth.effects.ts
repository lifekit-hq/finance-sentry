import {DOCUMENT} from '@angular/common';
import {effect, inject, type Signal, untracked} from '@angular/core';
import {NavigationEnd, Router} from '@angular/router';
import {ErrorMessageService} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, filter, pipe, startWith, switchMap, tap} from 'rxjs';

import {environment} from '../../../../environments/environment';
import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {ErrorUtils} from '../../../shared/utils/error.utils';
import {PushSessionService} from '../../settings/services/push-session.service';
import {SettingsService} from '../../settings/services/settings.service';
import {FALLBACK_SIGN_IN_METHODS} from '../constants/auth/auth.constants';
import {
  type AcceptInviteRequest,
  type AuthRequest,
  type AuthResponse,
  type SignInMethods,
} from '../models/auth/auth.model';
import {AuthService} from '../services/auth.service';
import {AuthUtils} from '../utils/auth.utils';
import {type AuthFlow, type FlashMessage} from './auth.state';

interface EffectsStore {
  applyAuthResponse: (res: AuthResponse) => void;
  clearSession: () => void;
  setProfileName: (firstName: Nullable<string>, lastName: Nullable<string>) => void;
  setLoading: (flow: AuthFlow) => void;
  setError: (errorCode: Nullable<string>, flow: AuthFlow, errorDetail?: Nullable<string>) => void;
  setReturnUrl: (returnUrl: Nullable<string>) => void;
  setFlashMessage: (flashMessage: Nullable<FlashMessage>) => void;
  setSignInMethods: (signInMethods: SignInMethods) => void;
  isAuthenticated: Signal<boolean>;
  returnUrl: Signal<Nullable<string>>;
}

interface HookStore extends EffectsStore {
  firstName: Signal<Nullable<string>>;
  loadProfileName: () => void;
  loadSignInMethods: () => void;
}

function flashFromParams(
  info: Nullable<string>,
  error: Nullable<string>,
  resolveCode: (code: string) => string | null
): Nullable<FlashMessage> {
  if (info === 'google_cancelled') {
    return {kind: 'info', text: 'Google sign-in was cancelled. Try again or use email/password.'};
  }
  if (error === 'google_failed') {
    return {kind: 'error', text: 'Google sign-in failed. Please try again.'};
  }
  // The OIDC callback bounces here with the API's error code (ACCOUNT_NOT_INVITED, ...).
  const text = error === null ? null : resolveCode(error);
  return text === null ? null : {kind: 'error', text};
}

export function authEffects(store: EffectsStore) {
  const authService = inject(AuthService);
  const router = inject(Router);
  const settingsService = inject(SettingsService);
  const pushSession = inject(PushSessionService);
  const doc = inject(DOCUMENT);

  return {
    login: rxMethod<AuthRequest>(
      pipe(
        tap(() => store.setLoading('login')),
        switchMap(req =>
          authService.login(req).pipe(
            tap(res => store.applyAuthResponse(res)),
            catchError((err: unknown) => {
              store.setError(ErrorUtils.extractCode(err), 'login', ErrorUtils.extractMessage(err));
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
    /** Hands the browser to the identity provider; the API redirects back signed in (or to /login with an error). */
    startOidcSignIn(): void {
      doc.location.assign(AuthUtils.oidcStartUrl(environment.apiBaseUrl, store.returnUrl()));
    },
    loadSignInMethods: rxMethod<void>(
      pipe(
        switchMap(() =>
          authService.getSignInMethods().pipe(
            tap(methods => store.setSignInMethods(methods)),
            catchError(() => {
              store.setSignInMethods(FALLBACK_SIGN_IN_METHODS);
              return EMPTY;
            })
          )
        )
      )
    ),
    loadProfileName: rxMethod<void>(
      pipe(
        switchMap(() =>
          settingsService.getProfile().pipe(
            filter(() => store.isAuthenticated()),
            tap(profile => store.setProfileName(profile.firstName, profile.lastName)),
            catchError(() => EMPTY)
          )
        )
      )
    ),
    logout(): void {
      if (store.isAuthenticated()) {
        pushSession.release().subscribe();
      }
      authService.logout().subscribe({error: () => undefined});
      store.clearSession();
      void router.navigate([AppRoute.Login]);
    },
  };
}

export function authHooks(store: HookStore): void {
  const router = inject(Router);
  const errorMessages = inject(ErrorMessageService);

  store.loadSignInMethods();

  router.events
    .pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      startWith(null)
    )
    .subscribe(() => {
      const params = router.routerState.root.snapshot.queryParamMap;
      store.setReturnUrl(params.get('returnUrl'));
      store.setFlashMessage(
        flashFromParams(params.get('info'), params.get('error'), code =>
          errorMessages.resolve(code)
        )
      );
    });

  effect(() => {
    if (!store.isAuthenticated()) {
      return;
    }
    untracked(() => {
      if (store.firstName() === null) {
        store.loadProfileName();
      }
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
