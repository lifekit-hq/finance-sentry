import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router, type UrlTree} from '@angular/router';
import {firstValueFrom, type Observable} from 'rxjs';
import {describe, expect, it} from 'vitest';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../store/auth.store';
import {guestGuard} from './guest.guard';

async function run(isAuthenticated: boolean, resolved = true): Promise<boolean | UrlTree> {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: AuthStore,
        useValue: {isAuthenticated: signal(isAuthenticated), sessionResolved: signal(resolved)},
      },
    ],
  });
  const result = TestBed.runInInjectionContext(() => guestGuard({} as never, {} as never));
  return firstValueFrom(result as Observable<boolean | UrlTree>);
}

describe('guestGuard', () => {
  it('lets a signed-out visitor reach the guest page', async () => {
    expect(await run(false)).toBe(true);
  });

  it('sends a signed-in person to the same landing route as the root', async () => {
    const result = await run(true);

    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe(AppRoute.Dashboard);
  });
});
