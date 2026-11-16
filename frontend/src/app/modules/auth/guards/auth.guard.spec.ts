import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router, type UrlTree} from '@angular/router';
import {describe, expect, it, vi} from 'vitest';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../store/auth.store';
import {authGuard} from './auth.guard';

function run(isAuthenticated: boolean, sessionResolved: boolean) {
  const deferAuthUrl = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: AuthStore,
        useValue: {
          isAuthenticated: signal(isAuthenticated),
          sessionResolved: signal(sessionResolved),
          deferAuthUrl,
        },
      },
    ],
  });
  const result = TestBed.runInInjectionContext(() =>
    authGuard({} as never, {url: '/accounts'} as never)
  ) as boolean | UrlTree;
  return {result, deferAuthUrl};
}

describe('authGuard', () => {
  it('lets a signed-in person through', () => {
    expect(run(true, true).result).toBe(true);
  });

  it('lets the navigation through while the session call is out, and records its URL', () => {
    const {result, deferAuthUrl} = run(false, false);

    expect(result).toBe(true);
    expect(deferAuthUrl).toHaveBeenCalledWith('/accounts');
  });

  it('sends a signed-out visitor to sign in once the session call has answered', () => {
    const {result} = run(false, true);

    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe(
      `${AppRoute.Login}?returnUrl=%2Faccounts`
    );
  });
});
