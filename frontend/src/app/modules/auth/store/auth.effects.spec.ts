import {DOCUMENT} from '@angular/common';
import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {NavigationEnd, Router} from '@angular/router';
import {ErrorMessageService} from '@lifekit-hq/core';
import {EMPTY} from 'rxjs';
import {of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {PushSessionService} from '../../settings/services/push-session.service';
import {SettingsService} from '../../settings/services/settings.service';
import {FALLBACK_SIGN_IN_METHODS} from '../constants/auth/auth.constants';
import {type AuthResponse, type SignInMethods} from '../models/auth/auth.model';
import {AuthService} from '../services/auth.service';
import {authEffects, authHooks} from './auth.effects';
import {type FlashMessage} from './auth.state';

const SAMPLE_RESPONSE: AuthResponse = {
  user: {id: 'u-1', email: 'user@test.com', roles: [], permissions: []},
  expiresAt: '2099-01-01T00:00:00Z',
};

function buildStore(overrides: {isAuthenticated?: boolean; returnUrl?: Nullable<string>} = {}) {
  const flashMessage = signal<Nullable<FlashMessage>>(null);
  return {
    applyAuthResponse: vi.fn(),
    clearSession: vi.fn(),
    setLoading: vi.fn(),
    setError: vi.fn(),
    setReturnUrl: vi.fn(),
    setFlashMessage: vi.fn((message: Nullable<FlashMessage>) => flashMessage.set(message)),
    setProfileName: vi.fn(),
    setSignInMethods: vi.fn(),
    loadProfileName: vi.fn(),
    loadSignInMethods: vi.fn(),
    firstName: signal<Nullable<string>>(null),
    isAuthenticated: signal(overrides.isAuthenticated ?? false),
    returnUrl: signal<Nullable<string>>(overrides.returnUrl ?? null),
    signInMethods: signal<Nullable<SignInMethods>>(null),
    flashMessage,
    startOidcSignIn: vi.fn(),
  };
}

function buildService() {
  return {
    login: vi.fn(),
    acceptInvite: vi.fn(),
    getSignInMethods: vi.fn().mockReturnValue(of(FALLBACK_SIGN_IN_METHODS)),
    logout: vi.fn().mockReturnValue(of(null)),
    refresh: vi.fn().mockReturnValue(throwError(() => new Error('no cookie'))),
  };
}

function buildRouter(url = '/login') {
  return {
    url,
    events: new Subject<unknown>(),
    navigate: vi.fn(),
    navigateByUrl: vi.fn(),
    routerState: {root: {snapshot: {queryParamMap: new Map<string, string>()}}},
  };
}

const pushSession = {release: vi.fn().mockReturnValue(EMPTY)};

function configure(
  service: ReturnType<typeof buildService>,
  router: ReturnType<typeof buildRouter>
) {
  TestBed.configureTestingModule({
    providers: [
      {provide: AuthService, useValue: service},
      {provide: SettingsService, useValue: {getProfile: () => EMPTY}},
      {provide: PushSessionService, useValue: pushSession},
      {provide: Router, useValue: router},
      {
        provide: ErrorMessageService,
        useValue: {
          resolve: (code: string) =>
            code === 'ACCOUNT_NOT_INVITED' ? 'No account exists for this email.' : null,
        },
      },
    ],
  });
}

describe('authEffects', () => {
  describe('login', () => {
    it('sets loading, calls service, and applies response on success', () => {
      const store = buildStore();
      const service = buildService();
      service.login.mockReturnValue(of(SAMPLE_RESPONSE));
      const router = buildRouter();
      configure(service, router);

      TestBed.runInInjectionContext(() => {
        const effects = authEffects(store);
        effects.login({email: 'a@b.c', password: 'pw'});
      });

      expect(store.setLoading).toHaveBeenCalledWith('login');
      expect(service.login).toHaveBeenCalledWith({email: 'a@b.c', password: 'pw'});
      expect(store.applyAuthResponse).toHaveBeenCalledWith(SAMPLE_RESPONSE);
      expect(store.setError).not.toHaveBeenCalled();
    });

    it('sets error with extracted errorCode on failure', () => {
      const store = buildStore();
      const service = buildService();
      service.login.mockReturnValue(
        throwError(() => ({error: {errorCode: 'INVALID_CREDENTIALS'}}))
      );
      const router = buildRouter();
      configure(service, router);

      TestBed.runInInjectionContext(() => {
        authEffects(store).login({email: 'a@b.c', password: 'pw'});
      });

      expect(store.setError).toHaveBeenCalledWith('INVALID_CREDENTIALS', 'login', null);
      expect(store.applyAuthResponse).not.toHaveBeenCalled();
    });

    it('passes the server message through for a lockout', () => {
      const store = buildStore();
      const service = buildService();
      const error = 'Too many failed sign-in attempts. Try again in 3 minutes.';
      service.login.mockReturnValue(
        throwError(() => ({error: {errorCode: 'ACCOUNT_LOCKED', error}}))
      );
      configure(service, buildRouter());

      TestBed.runInInjectionContext(() => {
        authEffects(store).login({email: 'a@b.c', password: 'pw'});
      });

      expect(store.setError).toHaveBeenCalledWith('ACCOUNT_LOCKED', 'login', error);
    });

    it('sets null errorCode when error payload is unstructured', () => {
      const store = buildStore();
      const service = buildService();
      service.login.mockReturnValue(throwError(() => new Error('boom')));
      configure(service, buildRouter());

      TestBed.runInInjectionContext(() => {
        authEffects(store).login({email: 'a@b.c', password: 'pw'});
      });

      expect(store.setError).toHaveBeenCalledWith(null, 'login', null);
    });
  });

  describe('acceptInvite', () => {
    const request = {userId: 'u-1', token: 'one-time-token', password: 'quiet lantern orchard'};

    it('tags the flow and applies the response on success', () => {
      const store = buildStore();
      const service = buildService();
      service.acceptInvite.mockReturnValue(of(SAMPLE_RESPONSE));
      configure(service, buildRouter());

      TestBed.runInInjectionContext(() => {
        authEffects(store).acceptInvite(request);
      });

      expect(service.acceptInvite).toHaveBeenCalledWith(request);
      expect(store.setLoading).toHaveBeenCalledWith('acceptInvite');
      expect(store.applyAuthResponse).toHaveBeenCalledWith(SAMPLE_RESPONSE);
    });

    it('records the error code under the acceptInvite flow on failure', () => {
      const store = buildStore();
      const service = buildService();
      service.acceptInvite.mockReturnValue(
        throwError(() => new HttpErrorResponse({status: 400, error: {errorCode: 'INVALID_INVITE'}}))
      );
      configure(service, buildRouter());

      TestBed.runInInjectionContext(() => {
        authEffects(store).acceptInvite(request);
      });

      expect(store.setError).toHaveBeenCalledWith('INVALID_INVITE', 'acceptInvite');
    });
  });

  describe('logout', () => {
    it('calls service.logout, clears session, and navigates to login', () => {
      const store = buildStore({isAuthenticated: true});
      const service = buildService();
      const router = buildRouter();
      configure(service, router);

      TestBed.runInInjectionContext(() => {
        authEffects(store).logout();
      });

      expect(pushSession.release).toHaveBeenCalled();
      expect(service.logout).toHaveBeenCalled();
      expect(store.clearSession).toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith([AppRoute.Login], {
        queryParams: {info: 'signed_out'},
      });
    });

    it('does not release push when the session is already cleared', () => {
      const store = buildStore({isAuthenticated: false});
      pushSession.release.mockClear();
      configure(buildService(), buildRouter());

      TestBed.runInInjectionContext(() => {
        authEffects(store).logout();
      });

      expect(pushSession.release).not.toHaveBeenCalled();
      expect(store.clearSession).toHaveBeenCalled();
    });

    it('does not throw when logout HTTP fails', () => {
      const store = buildStore();
      const service = buildService();
      service.logout.mockReturnValue(throwError(() => new Error('network')));
      const router = buildRouter();
      configure(service, router);

      expect(() => {
        TestBed.runInInjectionContext(() => authEffects(store).logout());
      }).not.toThrow();
      expect(store.clearSession).toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith([AppRoute.Login], {
        queryParams: {info: 'signed_out'},
      });
    });
  });

  describe('startOidcSignIn', () => {
    it('navigates the browser to the API start endpoint with the return path', () => {
      const assign = vi.fn();
      const store = buildStore({returnUrl: '/budgets'});
      configure(buildService(), buildRouter());
      TestBed.overrideProvider(DOCUMENT, {useValue: {location: {assign}}});

      TestBed.runInInjectionContext(() => authEffects(store).startOidcSignIn());

      expect(assign).toHaveBeenCalledWith(
        expect.stringMatching(/\/auth\/oidc\/start\?returnUrl=%2Fbudgets$/)
      );
    });
  });

  describe('loadSignInMethods', () => {
    it('stores the methods the API reports', () => {
      const store = buildStore();
      const service = buildService();
      const methods = {oidc: true, passwordLogin: false};
      service.getSignInMethods.mockReturnValue(of(methods));
      configure(service, buildRouter());

      TestBed.runInInjectionContext(() => authEffects(store).loadSignInMethods());

      expect(store.setSignInMethods).toHaveBeenCalledWith(methods);
    });

    it('falls back to the pre-OIDC methods when the request fails', () => {
      const store = buildStore();
      const service = buildService();
      service.getSignInMethods.mockReturnValue(throwError(() => new Error('down')));
      configure(service, buildRouter());

      TestBed.runInInjectionContext(() => authEffects(store).loadSignInMethods());

      expect(store.setSignInMethods).toHaveBeenCalledWith(FALLBACK_SIGN_IN_METHODS);
    });
  });

  describe('loadProfileName', () => {
    function configureWithProfile(profile$: Subject<{firstName: string; lastName: string}>) {
      TestBed.configureTestingModule({
        providers: [
          {provide: AuthService, useValue: buildService()},
          {provide: SettingsService, useValue: {getProfile: () => profile$}},
          {provide: Router, useValue: buildRouter()},
        ],
      });
    }

    it('stores the profile name while authenticated', () => {
      const store = buildStore({isAuthenticated: true});
      const profile$ = new Subject<{firstName: string; lastName: string}>();
      configureWithProfile(profile$);

      TestBed.runInInjectionContext(() => authEffects(store).loadProfileName());
      profile$.next({firstName: 'Denys', lastName: 'Tester'});

      expect(store.setProfileName).toHaveBeenCalledWith('Denys', 'Tester');
    });

    it('drops a late response after the session ended', () => {
      const store = buildStore({isAuthenticated: true});
      const profile$ = new Subject<{firstName: string; lastName: string}>();
      configureWithProfile(profile$);

      TestBed.runInInjectionContext(() => authEffects(store).loadProfileName());
      store.isAuthenticated.set(false);
      profile$.next({firstName: 'Denys', lastName: 'Tester'});

      expect(store.setProfileName).not.toHaveBeenCalled();
    });
  });
});

describe('authHooks', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  function setQueryParams(router: ReturnType<typeof buildRouter>, entries: [string, string][]) {
    const map = new Map(entries);
    router.routerState = {
      root: {
        snapshot: {
          queryParamMap: {get: (key: string) => map.get(key) ?? null} as unknown as Map<
            string,
            string
          >,
        },
      },
    };
  }

  it('reads returnUrl and flashMessage from current query params on init', () => {
    const store = buildStore();
    const router = buildRouter('/login');
    setQueryParams(router, [
      ['returnUrl', '/dashboard'],
      ['info', 'signed_out'],
    ]);
    configure(buildService(), router);

    TestBed.runInInjectionContext(() => authHooks(store));

    expect(store.setReturnUrl).toHaveBeenCalledWith('/dashboard');
    expect(store.setFlashMessage).toHaveBeenCalledWith({
      kind: 'info',
      text: 'You have been signed out.',
    });
  });

  it('loads the sign-in methods on init', () => {
    const store = buildStore();
    configure(buildService(), buildRouter('/login'));

    TestBed.runInInjectionContext(() => authHooks(store));

    expect(store.loadSignInMethods).toHaveBeenCalledOnce();
  });

  it('turns an API error code from the OIDC callback into a flash message', () => {
    const store = buildStore();
    const router = buildRouter('/login');
    setQueryParams(router, [['error', 'ACCOUNT_NOT_INVITED']]);
    configure(buildService(), router);

    TestBed.runInInjectionContext(() => authHooks(store));

    expect(store.setFlashMessage).toHaveBeenCalledWith({
      kind: 'error',
      text: 'No account exists for this email.',
    });
  });

  it('shows a generic flash for an error code the registry does not know', () => {
    const store = buildStore();
    const router = buildRouter('/login');
    setQueryParams(router, [['error', 'SOMETHING_ELSE']]);
    configure(buildService(), router);

    TestBed.runInInjectionContext(() => authHooks(store));

    expect(store.setFlashMessage).toHaveBeenCalledWith({
      kind: 'error',
      text: 'Sign-in failed. Please try again.',
    });
  });

  it('re-reads query params on NavigationEnd', () => {
    const store = buildStore();
    const router = buildRouter('/login');
    configure(buildService(), router);

    TestBed.runInInjectionContext(() => authHooks(store));
    store.setReturnUrl.mockClear();
    store.setFlashMessage.mockClear();

    setQueryParams(router, [['error', 'OIDC_FAILED']]);
    router.events.next(
      new NavigationEnd(1, '/login?error=OIDC_FAILED', '/login?error=OIDC_FAILED')
    );

    expect(store.setFlashMessage).toHaveBeenLastCalledWith({
      kind: 'error',
      text: expect.stringContaining('failed'),
    });
  });

  describe('OIDC-only forward', () => {
    const OIDC_ONLY: SignInMethods = {oidc: true, passwordLogin: false};

    function hooksOn(url: string, params: [string, string][] = [], isAuthenticated = false) {
      const store = buildStore({isAuthenticated});
      const router = buildRouter(url);
      setQueryParams(router, params);
      configure(buildService(), router);
      TestBed.runInInjectionContext(() => authHooks(store));
      return {store, router};
    }

    it('forwards /login straight to the identity provider once the methods say OIDC is the only way in', () => {
      const {store} = hooksOn('/login');

      store.signInMethods.set(OIDC_ONLY);
      TestBed.flushEffects();

      expect(store.startOidcSignIn).toHaveBeenCalledOnce();
    });

    it('keeps the form when password sign-in is still enabled', () => {
      const {store} = hooksOn('/login');

      store.signInMethods.set({oidc: true, passwordLogin: true});
      TestBed.flushEffects();

      expect(store.startOidcSignIn).not.toHaveBeenCalled();
    });

    it('does not forward before the methods are known', () => {
      const {store} = hooksOn('/login');

      TestBed.flushEffects();

      expect(store.startOidcSignIn).not.toHaveBeenCalled();
    });

    it.each([
      ['a failed return from the provider', 'error', 'OIDC_FAILED'],
      ['an unregistered error code', 'error', 'SOMETHING_ELSE'],
      ['a sign-out', 'info', 'signed_out'],
    ])('holds the forward after %s so the page can show it', (_label, key, value) => {
      const {store} = hooksOn(`/login?${key}=${value}`, [[key, value]]);

      store.signInMethods.set(OIDC_ONLY);
      TestBed.flushEffects();

      expect(store.startOidcSignIn).not.toHaveBeenCalled();
    });

    it('does not forward from other pages such as the invite page', () => {
      const {store} = hooksOn(`${AppRoute.AcceptInvite}?user=u-1&token=t`);

      store.signInMethods.set(OIDC_ONLY);
      TestBed.flushEffects();

      expect(store.startOidcSignIn).not.toHaveBeenCalled();
    });

    it('does not forward a signed-in session', () => {
      const {store} = hooksOn('/login', [], true);

      store.signInMethods.set(OIDC_ONLY);
      TestBed.flushEffects();

      expect(store.startOidcSignIn).not.toHaveBeenCalled();
    });
  });

  it('navigates to returnUrl when authenticated on /login', () => {
    const store = buildStore({isAuthenticated: false, returnUrl: '/dashboard'});
    const router = buildRouter('/login');
    configure(buildService(), router);

    TestBed.runInInjectionContext(() => authHooks(store));

    store.isAuthenticated.set(true);
    TestBed.flushEffects();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('falls back to /accounts when returnUrl is null', () => {
    const store = buildStore({isAuthenticated: false, returnUrl: null});
    const router = buildRouter('/login');
    configure(buildService(), router);

    TestBed.runInInjectionContext(() => authHooks(store));

    store.isAuthenticated.set(true);
    TestBed.flushEffects();

    expect(router.navigateByUrl).toHaveBeenCalledWith(AppRoute.Accounts);
  });

  it('navigates away from the accept-invite page once signed in', () => {
    const store = buildStore({isAuthenticated: false, returnUrl: null});
    const router = buildRouter(`${AppRoute.AcceptInvite}?user=u-1&token=t`);
    configure(buildService(), router);

    TestBed.runInInjectionContext(() => authHooks(store));

    store.isAuthenticated.set(true);
    TestBed.flushEffects();

    expect(router.navigateByUrl).toHaveBeenCalledWith(AppRoute.Accounts);
  });

  it('does not navigate when authenticated on a non-auth route', () => {
    const store = buildStore({isAuthenticated: false, returnUrl: null});
    const router = buildRouter('/accounts');
    configure(buildService(), router);

    TestBed.runInInjectionContext(() => authHooks(store));

    store.isAuthenticated.set(true);
    TestBed.flushEffects();

    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('loads the profile name once authenticated when it is not yet known', () => {
    const store = buildStore();
    TestBed.runInInjectionContext(() => authHooks(store));
    store.isAuthenticated.set(true);
    TestBed.tick();
    expect(store.loadProfileName).toHaveBeenCalledOnce();
  });

  it('skips the profile load when the name is already known', () => {
    const store = buildStore();
    store.firstName.set('Denys');
    TestBed.runInInjectionContext(() => authHooks(store));
    store.isAuthenticated.set(true);
    TestBed.tick();
    expect(store.loadProfileName).not.toHaveBeenCalled();
  });
});
