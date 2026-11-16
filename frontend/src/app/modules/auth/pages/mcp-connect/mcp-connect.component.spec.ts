import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, Router} from '@angular/router';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../../store/auth.store';
import {McpConnectComponent} from './mcp-connect.component';

describe('McpConnectComponent', () => {
  const sessionResolved = signal(false);
  const isAuthenticated = signal(false);
  const navigate = vi.fn().mockResolvedValue(true);

  beforeEach(() => {
    sessionResolved.set(false);
    isAuthenticated.set(false);
    navigate.mockClear();
    TestBed.configureTestingModule({
      providers: [
        {provide: AuthStore, useValue: {sessionResolved, isAuthenticated}},
        {
          provide: Router,
          useValue: {
            navigate,
            createUrlTree: () => ({toString: () => '/mcp/connect?redirectUri=r&state=s'}),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: new Map([
                ['redirectUri', 'r'],
                ['state', 's'],
              ]),
            },
          },
        },
      ],
    });
  });

  it('does not send a signed-in person to sign in while the session call is out', () => {
    isAuthenticated.set(false);
    TestBed.createComponent(McpConnectComponent);
    TestBed.tick();

    expect(navigate).not.toHaveBeenCalled();
  });

  it('sends a signed-out visitor to sign in with the hand-off as the return url once the session call answers', () => {
    TestBed.createComponent(McpConnectComponent);
    TestBed.tick();
    sessionResolved.set(true);
    TestBed.tick();

    expect(navigate).toHaveBeenCalledWith([AppRoute.Login], {
      queryParams: {returnUrl: '/mcp/connect?redirectUri=r&state=s'},
    });
  });
});
