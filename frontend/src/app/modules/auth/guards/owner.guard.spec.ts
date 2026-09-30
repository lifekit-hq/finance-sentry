import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type ActivatedRouteSnapshot, Router, type RouterStateSnapshot} from '@angular/router';
import {beforeEach, describe, expect, it} from 'vitest';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../store/auth.store';
import {ownerGuard} from './owner.guard';

describe('ownerGuard', () => {
  const isOwner = signal(false);

  function run() {
    return TestBed.runInInjectionContext(() =>
      ownerGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
    );
  }

  beforeEach(() => {
    isOwner.set(false);
    TestBed.configureTestingModule({providers: [{provide: AuthStore, useValue: {isOwner}}]});
  });

  it('lets the owner through', () => {
    isOwner.set(true);
    expect(run()).toBe(true);
  });

  it('redirects a user without the owner role to the dashboard', () => {
    const result = run();
    expect(TestBed.inject(Router).serializeUrl(result as never)).toBe(AppRoute.Dashboard);
  });
});
