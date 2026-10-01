import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../core/errors/error-messages.registry';
import {authComputed} from './auth.computed';
import {type AuthFlow} from './auth.state';

function build(
  overrides: Partial<{
    userId: Nullable<string>;
    status: AsyncStatus;
    errorCode: Nullable<string>;
    flow: AuthFlow;
    permissions: string[];
  }> = {}
) {
  return {
    userId: signal<Nullable<string>>(overrides.userId ?? null),
    status: signal<AsyncStatus>(overrides.status ?? 'idle'),
    errorCode: signal<Nullable<string>>(overrides.errorCode ?? null),
    flow: signal<AuthFlow>(overrides.flow ?? null),
    permissions: signal<string[]>(overrides.permissions ?? []),
  };
}

describe('authComputed', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY}],
    });
  });

  describe('isAuthenticated', () => {
    it('returns true when userId is set', () => {
      const store = build({userId: 'u-1'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).isAuthenticated()).toBe(true);
      });
    });

    it('returns false when userId is null', () => {
      const store = build({userId: null});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).isAuthenticated()).toBe(false);
      });
    });
  });

  describe('isLoading', () => {
    it('is true when status is loading', () => {
      const store = build({status: 'loading'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).isLoading()).toBe(true);
      });
    });

    it('is false otherwise', () => {
      const store = build({status: 'idle'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).isLoading()).toBe(false);
      });
    });
  });

  describe('canUseAi', () => {
    it('is true when the ai.use permission is held', () => {
      const store = build({permissions: ['connections.manage', 'ai.use']});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).canUseAi()).toBe(true);
      });
    });

    it('is false without the ai.use permission', () => {
      const store = build({permissions: ['connections.manage']});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).canUseAi()).toBe(false);
      });
    });
  });

  describe('canManageUsers', () => {
    it('is true when the users.manage permission is held', () => {
      const store = build({permissions: ['users.manage']});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).canManageUsers()).toBe(true);
      });
    });

    it('is false without the users.manage permission', () => {
      const store = build({permissions: ['ai.use']});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).canManageUsers()).toBe(false);
      });
    });
  });

  describe('errorMessage', () => {
    it('maps GOOGLE_ACCOUNT_ONLY to the google prompt', () => {
      const store = build({errorCode: 'GOOGLE_ACCOUNT_ONLY', flow: 'login'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).errorMessage()).toContain('Continue with Google');
      });
    });

    it('maps INVALID_INVITE through the registry', () => {
      const store = build({errorCode: 'INVALID_INVITE', flow: 'acceptInvite'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).errorMessage()).toContain(
          'invite link is invalid or has expired'
        );
      });
    });

    it('maps ACCOUNT_NOT_INVITED on the google flow', () => {
      const store = build({errorCode: 'ACCOUNT_NOT_INVITED', flow: 'google'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).errorMessage()).toContain('Ask the owner for an invite');
      });
    });

    it('returns generic invalid-credentials message for unknown code on login flow', () => {
      const store = build({errorCode: 'SOME_OTHER_CODE', flow: 'login'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).errorMessage()).toBe('Invalid email or password.');
      });
    });

    it('returns the accept-invite fallback for unknown code on acceptInvite flow', () => {
      const store = build({errorCode: 'SOME_OTHER_CODE', flow: 'acceptInvite'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).errorMessage()).toContain('Could not set your password');
      });
    });

    it('returns empty string for unknown code on google flow', () => {
      const store = build({errorCode: 'SOME_OTHER_CODE', flow: 'google'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).errorMessage()).toBe('');
      });
    });

    it('returns empty string when errorCode is null', () => {
      const store = build({errorCode: null, flow: 'login'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).errorMessage()).toBe('');
      });
    });
  });
});
