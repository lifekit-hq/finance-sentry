import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type PartialMatchRouteSnapshot, Router, type UrlSegment} from '@angular/router';
import {firstValueFrom, type Observable} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {Permission} from '../../../shared/enums/permission/permission.enum';
import {AuthStore} from '../store/auth.store';
import {permissionGuard} from './permission.guard';

describe('permissionGuard', () => {
  const permissions = signal<string[]>([]);

  const sessionResolved = signal(true);
  const isAuthenticated = signal(true);

  async function run() {
    const result = TestBed.runInInjectionContext(() =>
      permissionGuard(Permission.AiUse)({}, [] as UrlSegment[], {} as PartialMatchRouteSnapshot)
    );
    return firstValueFrom(result as Observable<unknown>);
  }

  beforeEach(() => {
    permissions.set([]);
    sessionResolved.set(true);
    TestBed.configureTestingModule({
      providers: [{provide: AuthStore, useValue: {permissions, sessionResolved, isAuthenticated}}],
    });
  });

  it('matches for a user holding the permission', async () => {
    permissions.set([Permission.ConnectionsManage, Permission.AiUse]);
    expect(await run()).toBe(true);
  });

  it('redirects a user without the permission to the dashboard', async () => {
    permissions.set([Permission.ConnectionsManage]);
    const result = await run();
    expect(TestBed.inject(Router).serializeUrl(result as never)).toBe(AppRoute.Dashboard);
  });

  it('matches while the session call is out, then sends a signed-in user without the permission to the dashboard', async () => {
    sessionResolved.set(false);
    isAuthenticated.set(false);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    expect(await run()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();

    isAuthenticated.set(true);
    sessionResolved.set(true);
    TestBed.tick();

    expect(navigate).toHaveBeenCalledTimes(1);
    expect(TestBed.inject(Router).serializeUrl(navigate.mock.calls[0][0] as never)).toBe(
      AppRoute.Dashboard
    );
  });

  it('leaves a signed-out visitor to the sign-in redirect once the session call answers', async () => {
    sessionResolved.set(false);
    isAuthenticated.set(false);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    expect(await run()).toBe(true);
    sessionResolved.set(true);
    TestBed.tick();

    expect(navigate).not.toHaveBeenCalled();
  });

  it('keeps a signed-in user holding the permission on the route once the session call answers', async () => {
    sessionResolved.set(false);
    isAuthenticated.set(true);
    permissions.set([Permission.AiUse]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    expect(await run()).toBe(true);
    sessionResolved.set(true);
    TestBed.tick();

    expect(navigate).not.toHaveBeenCalled();
  });
});
