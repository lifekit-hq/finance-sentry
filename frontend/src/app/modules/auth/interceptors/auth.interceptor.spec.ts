import {
  HttpClient,
  HttpStatusCode,
  provideHttpClient,
  withInterceptors,
  withXhr,
} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {inject} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {AuthService} from '../services/auth.service';
import {AuthStore} from '../store/auth.store';
import {authInterceptor} from './auth.interceptor';

describe('authInterceptor', () => {
  const logout = vi.fn();
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    logout.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr(), withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        {provide: AuthService, useValue: {}},
        {provide: AuthStore, useValue: {logout, applyAuthResponse: vi.fn()}},
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('does not log out when the anonymous session-restore probe gets 401', () => {
    http.get('/api/v1/auth/me').subscribe({error: () => undefined});
    httpMock
      .expectOne('/api/v1/auth/me')
      .flush(null, {status: HttpStatusCode.Unauthorized, statusText: 'Unauthorized'});

    expect(logout).not.toHaveBeenCalled();
  });

  it('logs out when another auth endpoint answers 401', () => {
    http.post('/api/v1/auth/refresh', null).subscribe({error: () => undefined});
    httpMock
      .expectOne('/api/v1/auth/refresh')
      .flush(null, {status: HttpStatusCode.Unauthorized, statusText: 'Unauthorized'});

    expect(logout).toHaveBeenCalledTimes(1);
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
            return {logout, applyAuthResponse: vi.fn()};
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
