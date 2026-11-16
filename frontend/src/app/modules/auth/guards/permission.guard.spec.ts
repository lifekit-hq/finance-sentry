import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type PartialMatchRouteSnapshot, Router, type UrlSegment} from '@angular/router';
import {firstValueFrom, type Observable} from 'rxjs';
import {beforeEach, describe, expect, it} from 'vitest';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {Permission} from '../../../shared/enums/permission/permission.enum';
import {AuthStore} from '../store/auth.store';
import {permissionGuard} from './permission.guard';

describe('permissionGuard', () => {
  const permissions = signal<string[]>([]);

  const sessionResolved = signal(true);

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
      providers: [{provide: AuthStore, useValue: {permissions, sessionResolved}}],
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

  it('waits for the session call before deciding', async () => {
    sessionResolved.set(false);
    permissions.set([Permission.AiUse]);
    const pending = run();
    sessionResolved.set(true);
    TestBed.tick();

    expect(await pending).toBe(true);
  });
});
