import {TestBed} from '@angular/core/testing';
import {Router} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {ThemeService} from '@lifekit-hq/ui';
import {of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type AuthResponse} from '../../modules/auth/models/auth/auth.model';
import {AuthService} from '../../modules/auth/services/auth.service';
import {AuthStore} from '../../modules/auth/store/auth.store';
import {AppRoute} from '../../shared/enums/app-route/app-route.enum';
import {ERROR_MESSAGES_REGISTRY} from '../errors/error-messages.registry';
import {provideAppInit} from './app-init.provider';

const ME_RESPONSE: AuthResponse = {
  user: {id: 'u-1', email: 'user@test.com', roles: [], permissions: []},
  expiresAt: '2099-01-01T00:00:00Z',
};

function boot(getMe: unknown) {
  const navigate = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      {
        provide: AuthService,
        useValue: {
          getMe: vi.fn().mockReturnValue(getMe),
          getSignInMethods: vi.fn().mockReturnValue(of({oidc: true})),
        },
      },
      {
        provide: Router,
        useValue: {
          url: '/',
          events: new Subject<unknown>(),
          navigate,
          navigateByUrl: vi.fn(),
          routerState: {root: {snapshot: {queryParamMap: {get: () => null}}}},
        },
      },
      {provide: ThemeService, useValue: {}},
      {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      provideAppInit(),
    ],
  });
  const store = TestBed.inject(AuthStore);
  return {store, navigate};
}

describe('provideAppInit', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not hold bootstrap on the session call and leaves the session unresolved until it answers', () => {
    const me = new Subject<AuthResponse>();
    const {store, navigate} = boot(me);

    expect(store.sessionResolved()).toBe(false);
    expect(navigate).not.toHaveBeenCalled();

    me.next(ME_RESPONSE);
    me.complete();

    expect(store.sessionResolved()).toBe(true);
  });

  it('sends a signed-out deep link to sign in with its return URL once the call fails', () => {
    const me = new Subject<AuthResponse>();
    const {store, navigate} = boot(me);
    store.deferAuthUrl('/accounts?tab=1');

    me.error(new Error('401'));

    expect(store.sessionResolved()).toBe(true);
    expect(store.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith([AppRoute.Login], {
      queryParams: {returnUrl: '/accounts?tab=1'},
    });
  });

  it('resolves the session without navigating when the call fails and no deep link was deferred', () => {
    const {store, navigate} = boot(throwError(() => new Error('401')));

    expect(store.sessionResolved()).toBe(true);
    expect(store.isAuthenticated()).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('keeps a signed-in deep link where it is once the call succeeds', () => {
    const me = new Subject<AuthResponse>();
    const {store, navigate} = boot(me);
    store.deferAuthUrl('/accounts');

    me.next(ME_RESPONSE);
    me.complete();

    expect(store.sessionResolved()).toBe(true);
    expect(store.isAuthenticated()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });
});
