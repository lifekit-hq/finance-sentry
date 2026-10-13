import {DOCUMENT} from '@angular/common';
import {effect, inject, type Signal, signal, untracked} from '@angular/core';
import {NavigationEnd, Router} from '@angular/router';
import {ErrorMessageService} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, filter, of, pipe, startWith, switchMap, tap} from 'rxjs';

import {environment} from '../../../../environments/environment';
import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {AppPreferencesStore} from '../../../shared/store/preferences/app-preferences.store';
import {PushSessionService} from '../../settings/services/push-session.service';
import {SIGNED_OUT_INFO} from '../constants/auth/auth.constants';
import {type AuthResponse, type SignInMethods} from '../models/auth/auth.model';
import {AuthService} from '../services/auth.service';
import {AuthUtils} from '../utils/auth.utils';
import {type FlashMessage, type SignInMethodsStatus} from './auth.state';

interface EffectsStore {
  applyAuthResponse: (res: AuthResponse) => void;
  clearSession: () => void;
  setReturnUrl: (returnUrl: Nullable<string>) => void;
  setFlashMessage: (flashMessage: Nullable<FlashMessage>) => void;
  setSignInMethods: (signInMethods: SignInMethods) => void;
  setSignInMethodsStatus: (status: SignInMethodsStatus) => void;
  isAuthenticated: Signal<boolean>;
  returnUrl: Signal<Nullable<string>>;
}

interface HookStore extends EffectsStore {
  signInMethods: Signal<Nullable<SignInMethods>>;
  flashMessage: Signal<Nullable<FlashMessage>>;
  startOidcSignIn: () => void;
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
  const pushSession = inject(PushSessionService);
  const doc = inject(DOCUMENT);
  let signingOut = false;

  const releasePush = (): void => {
    if (store.isAuthenticated()) {
      pushSession.release().subscribe();
    }
  };
  const goToLogin = (): void => {
    void router.navigate([AppRoute.Login], {queryParams: {info: SIGNED_OUT_INFO}});
  };

  return {
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
    /** The person's own Sign out: ends the identity provider's session too, so the next sign-in asks for credentials. */
    logout(): void {
      if (signingOut) {
        return;
      }
      signingOut = true;
      releasePush();
      // Navigate only once the API has answered: /login forwards straight to the identity provider, so landing
      // there before the provider's session is ended would sign the person right back in.
      authService
        .logout()
        .pipe(catchError(() => of(null)))
        .subscribe(res => {
          store.clearSession();
          if (res?.endSessionUrl) {
            doc.location.assign(res.endSessionUrl);
          } else {
            signingOut = false;
            goToLogin();
          }
        });
    },
    /** A lost or revoked app session: clears it and returns to /login, which re-enters through the provider. */
    expireSession(): void {
      if (signingOut) {
        return;
      }
      releasePush();
      authService.logout().subscribe({error: () => undefined});
      store.clearSession();
      goToLogin();
    },
  };
}

export function authHooks(store: HookStore): void {
  const loginRoute: string = AppRoute.Login;
  const router = inject(Router);
  const errorMessages = inject(ErrorMessageService);
  const preferences = inject(AppPreferencesStore);

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
    if (!onLogin || !methods?.oidc || store.flashMessage() !== null) {
      return;
    }
    untracked(() => {
      if (!store.isAuthenticated()) {
        store.startOidcSignIn();
      }
    });
  });

  // The account's preferences (appearance) follow the session: read once it is signed in, forgotten when it
  // ends so the next person starts clean.
  effect(() => {
    const authenticated = store.isAuthenticated();
    untracked(() => {
      if (authenticated) {
        void preferences.load();
      } else {
        preferences.clear();
      }
    });
  });

  effect(() => {
    if (!store.isAuthenticated()) {
      return;
    }
    untracked(() => {
      const target = store.returnUrl() ?? AppRoute.Dashboard;
      const currentPath = pathOf(router.url);
      if (currentPath === loginRoute) {
        void router.navigateByUrl(target);
      }
    });
  });
}
