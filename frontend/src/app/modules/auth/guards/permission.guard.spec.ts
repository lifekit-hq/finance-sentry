import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type Route, Router, type UrlSegment} from '@angular/router';
import {beforeEach, describe, expect, it} from 'vitest';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {Permission} from '../../../shared/enums/permission/permission.enum';
import {AuthStore} from '../store/auth.store';
import {permissionGuard} from './permission.guard';

describe('permissionGuard', () => {
  const permissions = signal<string[]>([]);

  function run() {
    return TestBed.runInInjectionContext(() =>
      permissionGuard(Permission.AiUse)({} as Route, [] as UrlSegment[])
    );
  }

  beforeEach(() => {
    permissions.set([]);
    TestBed.configureTestingModule({providers: [{provide: AuthStore, useValue: {permissions}}]});
  });

  it('matches for a user holding the permission', () => {
    permissions.set([Permission.ConnectionsManage, Permission.AiUse]);
    expect(run()).toBe(true);
  });

  it('redirects a user without the permission to the dashboard', () => {
    permissions.set([Permission.ConnectionsManage]);
    const result = run();
    expect(TestBed.inject(Router).serializeUrl(result as never)).toBe(AppRoute.Dashboard);
  });
});
