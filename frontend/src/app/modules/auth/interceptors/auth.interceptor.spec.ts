import {
  HttpClient,
  HttpContext,
  HttpErrorResponse,
  HttpStatusCode,
  provideHttpClient,
  withInterceptors,
  withXhr,
} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {inject} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {Subject} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {SESSION_PROBE, SKIP_AUTH_REFRESH} from '../constants/auth/auth-http-context.constants';
import {type AuthResponse} from '../models/auth/auth.model';
import {AuthService} from '../services/auth.service';
import {AuthStore} from '../store/auth.store';
import {authInterceptor} from './auth.interceptor';

const UNAUTHORIZED = {status: HttpStatusCode.Unauthorized, statusText: 'Unauthorized'};
const SESSION_RESPONSE = {user: {}, expiresAt: ''} as unknown as AuthResponse;

describe('authInterceptor', () => {
  const expireSession = vi.fn();
  const applyAuthResponse = vi.fn();
  const refresh = vi.fn();
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    expireSession.mockReset();
    applyAuthResponse.mockReset();
    refresh.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr(), withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        {provide: AuthService, useValue: {refresh}},
        {provide: AuthStore, useValue: {expireSession, applyAuthResponse}},
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('does not log out when the anonymous session-restore probe gets 401', () => {
    const context = new HttpContext().set(SKIP_AUTH_REFRESH, true).set(SESSION_PROBE, true);
    http.get('/api/v1/auth/me', {context}).subscribe({error: () => undefined});
    httpMock.expectOne('/api/v1/auth/me').flush(null, UNAUTHORIZED);

    expect(expireSession).not.toHaveBeenCalled();
    expect(refresh).not.toHaveBeenCalled();
  });

  it('logs out, without refreshing, when a token-marked request answers 401', () => {
    const context = new HttpContext().set(SKIP_AUTH_REFRESH, true);
    http.post('/api/v1/auth/login', null, {context}).subscribe({error: () => undefined});
    httpMock.expectOne('/api/v1/auth/login').flush(null, UNAUTHORIZED);

    expect(expireSession).toHaveBeenCalledTimes(1);
    expect(refresh).not.toHaveBeenCalled();
  });

  it('handles a non-auth URL containing /auth/ as a normal request', () => {
    const session$ = new Subject<AuthResponse>();
    refresh.mockReturnValue(session$);
    let body: unknown;
    http.get('/api/v1/oauth/auth/callback').subscribe(res => (body = res));

    httpMock.expectOne('/api/v1/oauth/auth/callback').flush(null, UNAUTHORIZED);
    expect(refresh).toHaveBeenCalledTimes(1);
    session$.next(SESSION_RESPONSE);
    session$.complete();
    httpMock.expectOne('/api/v1/oauth/auth/callback').flush({ok: true});

    expect(body).toEqual({ok: true});
    expect(applyAuthResponse).toHaveBeenCalledWith(SESSION_RESPONSE);
  });

  it('shares one refresh between parallel 401s and retries them all', () => {
    const session$ = new Subject<AuthResponse>();
    refresh.mockReturnValue(session$);
    const bodies: unknown[] = [];
    ['/api/v1/a', '/api/v1/b', '/api/v1/c'].forEach(url =>
      http.get(url).subscribe(res => bodies.push(res))
    );

    ['/api/v1/a', '/api/v1/b', '/api/v1/c'].forEach(url =>
      httpMock.expectOne(url).flush(null, UNAUTHORIZED)
    );
    expect(refresh).toHaveBeenCalledTimes(1);

    session$.next(SESSION_RESPONSE);
    session$.complete();
    ['/api/v1/a', '/api/v1/b', '/api/v1/c'].forEach(url => httpMock.expectOne(url).flush({url}));

    expect(bodies).toHaveLength(3);
    expect(applyAuthResponse).toHaveBeenCalledTimes(1);
    expect(expireSession).not.toHaveBeenCalled();
  });

  it('starts a fresh refresh for a 401 after the previous one completed', () => {
    const first$ = new Subject<AuthResponse>();
    const second$ = new Subject<AuthResponse>();
    refresh.mockReturnValueOnce(first$).mockReturnValueOnce(second$);

    http.get('/api/v1/a').subscribe();
    httpMock.expectOne('/api/v1/a').flush(null, UNAUTHORIZED);
    first$.next(SESSION_RESPONSE);
    first$.complete();
    httpMock.expectOne('/api/v1/a').flush({});

    http.get('/api/v1/b').subscribe();
    httpMock.expectOne('/api/v1/b').flush(null, UNAUTHORIZED);
    second$.next(SESSION_RESPONSE);
    second$.complete();
    httpMock.expectOne('/api/v1/b').flush({});

    expect(refresh).toHaveBeenCalledTimes(2);
  });

  it('logs out once when the refresh call itself answers 401', () => {
    const context = new HttpContext().set(SKIP_AUTH_REFRESH, true);
    refresh.mockImplementation(() =>
      http.post<AuthResponse>('/api/v1/auth/refresh', null, {context})
    );
    http.get('/api/v1/a').subscribe({error: () => undefined});
    http.get('/api/v1/b').subscribe({error: () => undefined});
    httpMock.expectOne('/api/v1/a').flush(null, UNAUTHORIZED);
    httpMock.expectOne('/api/v1/b').flush(null, UNAUTHORIZED);

    httpMock.expectOne('/api/v1/auth/refresh').flush(null, UNAUTHORIZED);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(expireSession).toHaveBeenCalledTimes(1);
  });

  it('logs out once when the shared refresh fails', () => {
    const session$ = new Subject<AuthResponse>();
    refresh.mockReturnValue(session$);
    const errors: unknown[] = [];
    ['/api/v1/a', '/api/v1/b', '/api/v1/c'].forEach(url =>
      http.get(url).subscribe({error: (err: unknown) => errors.push(err)})
    );
    ['/api/v1/a', '/api/v1/b', '/api/v1/c'].forEach(url =>
      httpMock.expectOne(url).flush(null, UNAUTHORIZED)
    );

    session$.error(new HttpErrorResponse({status: HttpStatusCode.InternalServerError}));

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(expireSession).toHaveBeenCalledTimes(1);
    expect(errors).toHaveLength(3);
    expect(
      errors.every(e => (e as HttpErrorResponse).status === (HttpStatusCode.Unauthorized as number))
    ).toBe(true);
  });

  it('lets a request issued while AuthStore is still being constructed through', () => {
    TestBed.resetTestingModule();
    let storeRequestError: unknown = null;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr(), withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        {provide: AuthService, useValue: {}},
        {
          // Like the real store, which loads the sign-in methods from its onInit hook.
          provide: AuthStore,
          useFactory: () => {
            inject(HttpClient)
              .get('/api/v1/auth/methods')
              .subscribe({error: (err: unknown) => (storeRequestError = err)});
            return {expireSession, applyAuthResponse: vi.fn()};
          },
        },
      ],
    });
    const mock = TestBed.inject(HttpTestingController);

    TestBed.inject(AuthStore);

    mock.expectOne('/api/v1/auth/methods').flush({oidc: true});
    expect(storeRequestError).toBeNull();
    mock.verify();
  });
});
