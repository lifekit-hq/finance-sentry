import {DOCUMENT} from '@angular/common';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {NavigationEnd, Router} from '@angular/router';
import {ErrorMessageService} from '@lifekit-hq/core';
import {EMPTY} from 'rxjs';
import {of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {AppPreferencesStore} from '../../../shared/store/preferences/app-preferences.store';
import {PushSessionService} from '../../settings/services/push-session.service';
import {type SignInMethods} from '../models/auth/auth.model';
import {AuthService} from '../services/auth.service';
import {authEffects, authHooks} from './auth.effects';
import {type FlashMessage} from './auth.state';

function buildStore(overrides: {isAuthenticated?: boolean; returnUrl?: Nullable<string>} = {}) {
  const flashMessage = signal<Nullable<FlashMessage>>(null);
  return {
    applyAuthResponse: vi.fn(),
    clearSession: vi.fn(),
    setReturnUrl: vi.fn(),
    setFlashMessage: vi.fn((message: Nullable<FlashMessage>) => flashMessage.set(message)),
    setSignInMethods: vi.fn(),
    setSignInMethodsStatus: vi.fn(),
    loadSignInMethods: vi.fn(),
    isAuthenticated: signal(overrides.isAuthenticated ?? false),
    returnUrl: signal<Nullable<string>>(overrides.returnUrl ?? null),
    signInMethods: signal<Nullable<SignInMethods>>(null),
    flashMessage,
    startOidcSignIn: vi.fn(),
  };
}

function buildService() {
  return {
    getSignInMethods: vi.fn().mockReturnValue(of({oidc: true})),
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
const preferences = {load: vi.fn().mockResolvedValue(undefined), clear: vi.fn()};

function configure(
  service: ReturnType<typeof buildService>,
  router: ReturnType<typeof buildRouter>
) {
  TestBed.configureTestingModule({
    providers: [
      {provide: AuthService, useValue: service},
      {provide: PushSessionService, useValue: pushSession},
      {provide: AppPreferencesStore, useValue: preferences},
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

    it('ends the identity provider session by navigating to the URL the API returns', () => {
      const assign = vi.fn();
      const store = buildStore({isAuthenticated: true});
      const service = buildService();
      service.logout.mockReturnValue(of({endSessionUrl: 'https://idp.test/oidc/session/end?x=1'}));
      const router = buildRouter();
      configure(service, router);
      TestBed.overrideProvider(DOCUMENT, {useValue: {location: {assign}}});

      TestBed.runInInjectionContext(() => authEffects(store).logout());

      expect(store.clearSession).toHaveBeenCalled();
      expect(assign).toHaveBeenCalledWith('https://idp.test/oidc/session/end?x=1');
      expect(router.navigate).not.toHaveBeenCalled();
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

  describe('logout and expireSession', () => {
    const endSession = {endSessionUrl: 'https://idp.test/oidc/session/end?x=1'};

    it('a repeated sign-out while one is in flight calls the API once', () => {
      const assign = vi.fn();
      const store = buildStore({isAuthenticated: true});
      const service = buildService();
      service.logout.mockReturnValue(of(endSession));
      configure(service, buildRouter());
      TestBed.overrideProvider(DOCUMENT, {useValue: {location: {assign}}});

      TestBed.runInInjectionContext(() => {
        const effects = authEffects(store);
        effects.logout();
        effects.logout();
      });

      expect(service.logout).toHaveBeenCalledTimes(1);
      expect(assign).toHaveBeenCalledTimes(1);
    });

    it('an expired session after a sign-out cannot navigate away from the end-session hop', () => {
      const assign = vi.fn();
      const store = buildStore({isAuthenticated: true});
      const service = buildService();
      service.logout.mockReturnValue(of(endSession));
      const router = buildRouter();
      configure(service, router);
      TestBed.overrideProvider(DOCUMENT, {useValue: {location: {assign}}});

      TestBed.runInInjectionContext(() => {
        const effects = authEffects(store);
        effects.logout();
        effects.expireSession();
      });

      expect(service.logout).toHaveBeenCalledTimes(1);
      expect(assign).toHaveBeenCalledWith(endSession.endSessionUrl);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('an expired session clears the app session and goes to /login without an end-session hop', () => {
      const assign = vi.fn();
      const store = buildStore({isAuthenticated: true});
      const service = buildService();
      service.logout.mockReturnValue(of(endSession));
      const router = buildRouter();
      configure(service, router);
      TestBed.overrideProvider(DOCUMENT, {useValue: {location: {assign}}});

      TestBed.runInInjectionContext(() => authEffects(store).expireSession());

      expect(store.clearSession).toHaveBeenCalled();
      expect(assign).not.toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith([AppRoute.Login], {
        queryParams: {info: 'signed_out'},
      });
    });

    it('a sign-out can be retried after the API answered without an end-session URL', () => {
      const store = buildStore({isAuthenticated: true});
      const service = buildService();
      const router = buildRouter();
      configure(service, router);

      TestBed.runInInjectionContext(() => {
        const effects = authEffects(store);
        effects.logout();
        effects.logout();
      });

      expect(service.logout).toHaveBeenCalledTimes(2);
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
      const methods = {oidc: true};
      service.getSignInMethods.mockReturnValue(of(methods));
      configure(service, buildRouter());

      TestBed.runInInjectionContext(() => authEffects(store).loadSignInMethods());

      expect(store.setSignInMethods).toHaveBeenCalledWith(methods);
    });

    it('reports the failure instead of guessing a method set', () => {
      const store = buildStore();
      const service = buildService();
      service.getSignInMethods.mockReturnValue(throwError(() => new Error('down')));
      configure(service, buildRouter());

      TestBed.runInInjectionContext(() => authEffects(store).loadSignInMethods());

      expect(store.setSignInMethodsStatus).toHaveBeenCalledWith('loading');
      expect(store.setSignInMethodsStatus).toHaveBeenCalledWith('error');
      expect(store.setSignInMethods).not.toHaveBeenCalled();
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

  describe('account preferences', () => {
    beforeEach(() => {
      preferences.load.mockClear();
      preferences.clear.mockClear();
    });

    it('loads them once the session is signed in', () => {
      const store = buildStore({isAuthenticated: true});
      configure(buildService(), buildRouter('/accounts'));

      TestBed.runInInjectionContext(() => authHooks(store));
      TestBed.flushEffects();

      expect(preferences.load).toHaveBeenCalledOnce();
      expect(preferences.clear).not.toHaveBeenCalled();
    });

    it('forgets them when the session ends', () => {
      const store = buildStore({isAuthenticated: true});
      configure(buildService(), buildRouter('/accounts'));

      TestBed.runInInjectionContext(() => authHooks(store));
      TestBed.flushEffects();
      store.isAuthenticated.set(false);
      TestBed.flushEffects();

      expect(preferences.clear).toHaveBeenCalledOnce();
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
    const OIDC_ONLY: SignInMethods = {oidc: true};

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

    it('does not forward when the API reports no OIDC sign-in', () => {
      const {store} = hooksOn('/login');

      store.signInMethods.set({oidc: false});
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

    it('does not forward from other pages such as the MCP connect page', () => {
      const {store} = hooksOn(AppRoute.McpConnect);

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

  it('falls back to the dashboard, the root landing, when returnUrl is null', () => {
    const store = buildStore({isAuthenticated: false, returnUrl: null});
    const router = buildRouter('/login');
    configure(buildService(), router);

    TestBed.runInInjectionContext(() => authHooks(store));

    store.isAuthenticated.set(true);
    TestBed.flushEffects();

    expect(router.navigateByUrl).toHaveBeenCalledWith(AppRoute.Dashboard);
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
});
