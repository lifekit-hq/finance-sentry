import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router, type UrlTree} from '@angular/router';
import {describe, expect, it} from 'vitest';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../store/auth.store';
import {guestGuard} from './guest.guard';

function run(isAuthenticated: boolean): boolean | UrlTree {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {provide: AuthStore, useValue: {isAuthenticated: signal(isAuthenticated)}},
    ],
  });
  return TestBed.runInInjectionContext(() => guestGuard({} as never, {} as never)) as
    boolean | UrlTree;
}

describe('guestGuard', () => {
  it('lets a signed-out visitor reach the guest page', () => {
    expect(run(false)).toBe(true);
  });

  it('sends a signed-in person to the same landing route as the root', () => {
    const result = run(true);

    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe(AppRoute.Dashboard);
  });
});
