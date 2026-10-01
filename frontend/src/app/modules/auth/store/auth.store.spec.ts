import {TestBed} from '@angular/core/testing';
import {Router} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../core/errors/error-messages.registry';
import {type AuthResponse} from '../models/auth/auth.model';
import {AuthService} from '../services/auth.service';
import {AuthStore} from './auth.store';

const SAMPLE_RESPONSE: AuthResponse = {
  user: {id: 'u-1', email: 'user@test.com', roles: [], permissions: []},
  expiresAt: '2099-01-01T00:00:00Z',
};

function routerMock(url = '/login') {
  return {
    url,
    events: new Subject<unknown>(),
    navigate: vi.fn(),
    navigateByUrl: vi.fn(),
    routerState: {
      root: {snapshot: {queryParamMap: {get: () => null}}},
    },
  };
}

function authServiceMock(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    login: vi.fn(),
    acceptInvite: vi.fn(),
    verifyGoogleCredential: vi.fn(),
    refresh: vi.fn().mockReturnValue(throwError(() => new Error('no cookie'))),
    logout: vi.fn().mockReturnValue(of(null)),
    getMe: vi.fn().mockReturnValue(throwError(() => new Error('no session'))),
    ...overrides,
  };
}

function configure(authService: unknown, router = routerMock()) {
  TestBed.configureTestingModule({
    providers: [
      {provide: AuthService, useValue: authService},
      {provide: Router, useValue: router},
      {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
    ],
  });
}

describe('AuthStore (integration)', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('starts unauthenticated before getMe resolves', () => {
    const authService = authServiceMock();
    configure(authService);

    const store = TestBed.inject(AuthStore);

    expect(store.userId()).toBeNull();
    expect(store.isAuthenticated()).toBe(false);
  });

  it('successful login stores userId and email in state', () => {
    const authService = authServiceMock({login: vi.fn().mockReturnValue(of(SAMPLE_RESPONSE))});
    configure(authService);

    const store = TestBed.inject(AuthStore);
    store.login({email: 'a@b.c', password: 'pw'});

    expect(store.userId()).toBe(SAMPLE_RESPONSE.user.id);
    expect(store.email()).toBe(SAMPLE_RESPONSE.user.email);
    expect(store.isAuthenticated()).toBe(true);
    expect(store.errorMessage()).toBe('');
  });

  it('failed login maps error code through errorMessage computed', () => {
    const authService = authServiceMock({
      login: vi
        .fn()
        .mockReturnValue(throwError(() => ({error: {errorCode: 'GOOGLE_ACCOUNT_ONLY'}}))),
    });
    configure(authService);

    const store = TestBed.inject(AuthStore);
    store.login({email: 'a@b.c', password: 'pw'});

    expect(store.userId()).toBeNull();
    expect(store.errorMessage()).toContain('Continue with Google');
  });

  it('logout clears userId and email and navigates', () => {
    const authService = authServiceMock({login: vi.fn().mockReturnValue(of(SAMPLE_RESPONSE))});
    const router = routerMock();
    configure(authService, router);

    const store = TestBed.inject(AuthStore);
    store.login({email: 'a@b.c', password: 'pw'});
    expect(store.userId()).toBe(SAMPLE_RESPONSE.user.id);

    store.logout();

    expect(store.userId()).toBeNull();
    expect(store.email()).toBeNull();
    expect(router.navigate).toHaveBeenCalled();
  });
});
