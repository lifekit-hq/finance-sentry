import {
  HttpClient,
  HttpStatusCode,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
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
        provideHttpClient(withInterceptors([authInterceptor])),
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
});
