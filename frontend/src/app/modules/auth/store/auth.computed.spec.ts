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
    roles: string[];
  }> = {}
) {
  return {
    userId: signal<Nullable<string>>(overrides.userId ?? null),
    status: signal<AsyncStatus>(overrides.status ?? 'idle'),
    errorCode: signal<Nullable<string>>(overrides.errorCode ?? null),
    flow: signal<AuthFlow>(overrides.flow ?? null),
    roles: signal<string[]>(overrides.roles ?? []),
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

  describe('isOwner', () => {
    it('is true when the Owner role is held', () => {
      const store = build({roles: ['Owner']});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).isOwner()).toBe(true);
      });
    });

    it('is false without the Owner role', () => {
      const store = build({roles: []});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).isOwner()).toBe(false);
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

    it('maps DUPLICATE_EMAIL regardless of flow', () => {
      const store = build({errorCode: 'DUPLICATE_EMAIL', flow: 'register'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).errorMessage()).toBe('Email is already registered.');
      });
    });

    it('returns generic invalid-credentials message for unknown code on login flow', () => {
      const store = build({errorCode: 'SOME_OTHER_CODE', flow: 'login'});
      TestBed.runInInjectionContext(() => {
        expect(authComputed(store).errorMessage()).toBe('Invalid email or password.');
      });
    });

    it('returns empty string for unknown code on register flow', () => {
      const store = build({errorCode: 'SOME_OTHER_CODE', flow: 'register'});
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
