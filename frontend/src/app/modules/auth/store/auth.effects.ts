import {DOCUMENT} from '@angular/common';
import {effect, inject, type Signal, signal, untracked} from '@angular/core';
import {NavigationEnd, Router} from '@angular/router';
import {ErrorMessageService} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, filter, pipe, startWith, switchMap, tap} from 'rxjs';

import {environment} from '../../../../environments/environment';
import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {ErrorUtils} from '../../../shared/utils/error.utils';
import {PushSessionService} from '../../settings/services/push-session.service';
import {SettingsService} from '../../settings/services/settings.service';
import {SIGNED_OUT_INFO} from '../constants/auth/auth.constants';
import {
  type AcceptInviteRequest,
  type AuthRequest,
  type AuthResponse,
  type SignInMethods,
} from '../models/auth/auth.model';
import {AuthService} from '../services/auth.service';
import {AuthUtils} from '../utils/auth.utils';
import {type AuthFlow, type FlashMessage, type SignInMethodsStatus} from './auth.state';

interface EffectsStore {
  applyAuthResponse: (res: AuthResponse) => void;
  clearSession: () => void;
  setProfileName: (firstName: Nullable<string>, lastName: Nullable<string>) => void;
  setLoading: (flow: AuthFlow) => void;
  setError: (errorCode: Nullable<string>, flow: AuthFlow, errorDetail?: Nullable<string>) => void;
  setReturnUrl: (returnUrl: Nullable<string>) => void;
  setFlashMessage: (flashMessage: Nullable<FlashMessage>) => void;
  setSignInMethods: (signInMethods: SignInMethods) => void;
  setSignInMethodsStatus: (status: SignInMethodsStatus) => void;
  isAuthenticated: Signal<boolean>;
  returnUrl: Signal<Nullable<string>>;
}

interface HookStore extends EffectsStore {
  firstName: Signal<Nullable<string>>;
  signInMethods: Signal<Nullable<SignInMethods>>;
  flashMessage: Signal<Nullable<FlashMessage>>;
  startOidcSignIn: () => void;
  loadProfileName: () => void;
  loadSignInMethods: () => void;
}

const UNKNOWN_FAILURE_TEXT = 'Sign-in failed. Please try again.';

function flashFromParams(
  info: Nullable<string>,
  error: Nullable<string>,
  resolveCode: (code: string) => string | null
): Nullable<FlashMessage> {
  if (info === SIGNED_OUT_INFO) {
    return {kind: 'info', text: 'You have been signed out.'};
  }
  // The OIDC callback bounces here with the API's error code (ACCOUNT_NOT_INVITED, ...). Any code is a message:
  // an unregistered one must still hold the OIDC-only forward, or a failing sign-in would loop.
  return error === null ? null : {kind: 'error', text: resolveCode(error) ?? UNKNOWN_FAILURE_TEXT};
}

function pathOf(url: string): string {
  return url.split('?')[0];
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
    /** Hands the browser to the identity provider; the API redirects back signed in (or to /login with an error). */
    startOidcSignIn(): void {
      doc.location.assign(AuthUtils.oidcStartUrl(environment.apiBaseUrl, store.returnUrl()));
    },
    loadSignInMethods: rxMethod<void>(
      pipe(
        tap(() => store.setSignInMethodsStatus('loading')),
        switchMap(() =>
          authService.getSignInMethods().pipe(
            tap(methods => store.setSignInMethods(methods)),
            // No guessed fallback set: the login page shows the failure with a Retry.
            catchError(() => {
              store.setSignInMethodsStatus('error');
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
      void router.navigate([AppRoute.Login], {queryParams: {info: SIGNED_OUT_INFO}});
    },
  };
}

export function authHooks(store: HookStore): void {
  const loginRoute: string = AppRoute.Login;
  const router = inject(Router);
  const errorMessages = inject(ErrorMessageService);

  store.loadSignInMethods();

  const routePath = signal(pathOf(router.url));

  router.events
    .pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      startWith(null)
    )
    .subscribe(() => {
      const params = router.routerState.root.snapshot.queryParamMap;
      routePath.set(pathOf(router.url));
      store.setReturnUrl(params.get('returnUrl'));
      store.setFlashMessage(
        flashFromParams(params.get('info'), params.get('error'), code =>
          errorMessages.resolve(code)
        )
      );
    });

  // OIDC is the only way in: /login has no form, so it forwards straight to the identity provider. A message
  // on the page (a failed return, a sign-out) holds the forward so the person reads it and retries by hand.
  effect(() => {
    const methods = store.signInMethods();
    const onLogin = routePath() === loginRoute;
    if (!onLogin || !methods?.oidc || methods.passwordLogin || store.flashMessage() !== null) {
      return;
    }
    untracked(() => {
      if (!store.isAuthenticated()) {
        store.startOidcSignIn();
      }
    });
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
      const currentPath = pathOf(router.url);
      const acceptInvitePath: string = AppRoute.AcceptInvite;
      if (currentPath === loginRoute || currentPath === acceptInvitePath) {
        void router.navigateByUrl(target);
      }
    });
  });
}
